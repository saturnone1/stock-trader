using Microsoft.Extensions.Options;
using StockTrader.Application.Research;
using StockTrader.Configuration;
using StockTrader.Services.Financial;

namespace StockTrader.BackgroundServices;

public class FinancialSnapshotIngestionService : BackgroundService
{
    private readonly IFinancialCollectionStore _collectionStore;
    private readonly FinancialSnapshotFileParser _parser;
    private readonly FinancialSnapshotImportService _importService;
    private readonly SecFinancialSnapshotSyncService _vendorSyncService;
    private readonly FinancialDataPipelineSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinancialSnapshotIngestionService> _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public FinancialSnapshotIngestionService(
        IFinancialCollectionStore collectionStore,
        FinancialSnapshotFileParser parser,
        FinancialSnapshotImportService importService,
        SecFinancialSnapshotSyncService vendorSyncService,
        IOptions<FinancialDataPipelineSettings> settings,
        TimeProvider timeProvider,
        ILogger<FinancialSnapshotIngestionService> logger)
    {
        _collectionStore = collectionStore;
        _parser = parser;
        _importService = importService;
        _vendorSyncService = vendorSyncService;
        _settings = settings.Value;
        _timeProvider = timeProvider;
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

                if (await _collectionStore.HasCompletedRunAsync(
                        fileInfo.FullName,
                        fingerprint,
                        ct))
                {
                    skipped++;
                    continue;
                }

                var runId = await _collectionStore.StartOrRestartRunAsync(
                    Path.GetExtension(fileInfo.Name).TrimStart('.').ToUpperInvariant(),
                    fileInfo.FullName,
                    fingerprint,
                    UtcNow,
                    ct);

                try
                {
                    var parsed = await _parser.ParseFileAsync(fileInfo.FullName, ct);
                    var summary = await _importService.UpsertAsync(parsed, ct);
                    await _collectionStore.CompleteRunAsync(
                        runId,
                        summary.ImportedCount,
                        summary.SkippedCount,
                        warning: null,
                        UtcNow,
                        ct);

                    imported += summary.ImportedCount;
                    skipped += summary.SkippedCount;
                    processedFiles++;
                }
                catch (Exception ex)
                {
                    await _collectionStore.FailRunAsync(runId, ex.Message, UtcNow, ct);
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

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
