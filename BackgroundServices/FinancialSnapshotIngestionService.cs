using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Application.Research;
using StockTrader.Data;
using StockTrader.Models;
using StockTrader.Services.Financial;

namespace StockTrader.BackgroundServices;

public class FinancialSnapshotIngestionService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly FinancialSnapshotFileParser _parser;
    private readonly FinancialSnapshotImportService _importService;
    private readonly SecFinancialSnapshotSyncService _vendorSyncService;
    private readonly FinancialDataPipelineSettings _settings;
    private readonly ILogger<FinancialSnapshotIngestionService> _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public FinancialSnapshotIngestionService(
        IDbContextFactory<AppDbContext> dbFactory,
        FinancialSnapshotFileParser parser,
        FinancialSnapshotImportService importService,
        SecFinancialSnapshotSyncService vendorSyncService,
        IOptions<FinancialDataPipelineSettings> settings,
        ILogger<FinancialSnapshotIngestionService> logger)
    {
        _dbFactory = dbFactory;
        _parser = parser;
        _importService = importService;
        _vendorSyncService = vendorSyncService;
        _settings = settings.Value;
        _logger = logger;
    }

    public string GetResolvedImportDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ImportDirectory))
            return Path.IsPathRooted(_settings.ImportDirectory)
                ? _settings.ImportDirectory
                : Path.GetFullPath(_settings.ImportDirectory);

        return Directory.Exists("/data")
            ? "/data/financial-imports"
            : Path.Combine(AppContext.BaseDirectory, "financial-imports");
    }

    public int ScanIntervalMinutes => Math.Max(1, _settings.ScanIntervalMinutes);
    public bool Enabled => _settings.Enabled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(GetResolvedImportDirectory());

        if (!_settings.Enabled)
        {
            _logger.LogInformation("Financial snapshot pipeline disabled");
            return;
        }

        _logger.LogInformation("Financial snapshot pipeline started: {Directory} every {Minutes}m", GetResolvedImportDirectory(), ScanIntervalMinutes);

        await RunScanAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(ScanIntervalMinutes));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunScanAsync(stoppingToken);
        }
    }

    public async Task<FinancialPipelineRunSummary> RunScanAsync(CancellationToken ct)
    {
        if (!await _scanLock.WaitAsync(0, ct))
        {
            return new FinancialPipelineRunSummary
            {
                Status = "Skipped",
                Message = "A financial import scan is already running."
            };
        }

        try
        {
            var directory = GetResolvedImportDirectory();
            Directory.CreateDirectory(directory);

            var files = Directory.GetFiles(directory)
                .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path)
                .ToList();

            var imported = 0;
            var skipped = 0;
            var processedFiles = 0;

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var fingerprint = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";

                await using var db = await _dbFactory.CreateDbContextAsync(ct);
                var alreadyProcessed = await db.FinancialImportRuns
                    .AnyAsync(run => run.FilePath == fileInfo.FullName && run.Fingerprint == fingerprint && run.Status == "Completed", ct);

                if (alreadyProcessed)
                {
                    skipped++;
                    continue;
                }

                var run = await db.FinancialImportRuns
                    .FirstOrDefaultAsync(item => item.FilePath == fileInfo.FullName && item.Fingerprint == fingerprint, ct);

                if (run == null)
                {
                    run = new FinancialImportRun
                    {
                        SourceType = Path.GetExtension(fileInfo.Name).TrimStart('.').ToUpperInvariant(),
                        FilePath = fileInfo.FullName,
                        Fingerprint = fingerprint,
                        Status = "Running",
                        StartedAt = DateTime.UtcNow
                    };
                    db.FinancialImportRuns.Add(run);
                }
                else
                {
                    run.SourceType = Path.GetExtension(fileInfo.Name).TrimStart('.').ToUpperInvariant();
                    run.Status = "Running";
                    run.ErrorMessage = null;
                    run.ImportedCount = 0;
                    run.SkippedCount = 0;
                    run.StartedAt = DateTime.UtcNow;
                    run.CompletedAt = null;
                }

                await db.SaveChangesAsync(ct);

                try
                {
                    var parsed = await _parser.ParseFileAsync(fileInfo.FullName, ct);
                    var summary = await _importService.UpsertAsync(parsed, ct);
                    run.Status = "Completed";
                    run.ImportedCount = summary.ImportedCount;
                    run.SkippedCount = summary.SkippedCount;
                    run.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    imported += summary.ImportedCount;
                    skipped += summary.SkippedCount;
                    processedFiles++;
                }
                catch (Exception ex)
                {
                    run.Status = "Failed";
                    run.ErrorMessage = ex.Message;
                    run.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    _logger.LogError(ex, "Financial snapshot import failed for {File}", fileInfo.FullName);
                }
            }

            var vendorSummary = await _vendorSyncService.RunConfiguredSyncAsync(ct);
            imported += vendorSummary.ImportedCount;
            skipped += vendorSummary.SkippedCount;
            processedFiles += vendorSummary.ProcessedFiles;

            var messageParts = new List<string>();
            if (processedFiles > 0)
                messageParts.Add($"Processed {processedFiles} item(s).");
            if (messageParts.Count == 0)
                messageParts.Add("No new financial import files found.");
            if (vendorSummary.Status != "Skipped" && !string.IsNullOrWhiteSpace(vendorSummary.Message))
                messageParts.Add(vendorSummary.Message);

            return new FinancialPipelineRunSummary
            {
                Status = "Completed",
                Message = string.Join(" ", messageParts),
                ImportedCount = imported,
                SkippedCount = skipped,
                ProcessedFiles = processedFiles
            };
        }
        finally
        {
            _scanLock.Release();
        }
    }
}

public class FinancialPipelineRunSummary
{
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ProcessedFiles { get; set; }
}
