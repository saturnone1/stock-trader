using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Models;

namespace StockTrader.Api;

public static class FinancialFactorEndpoints
{
    public static RouteGroupBuilder MapFinancialFactorApi(this RouteGroupBuilder group)
    {
        group.MapGet("/financial-factors/meta", async (AppDbContext db, CancellationToken ct) =>
        {
            var snapshots = await db.FinancialSnapshots
                .AsNoTracking()
                .OrderByDescending(x => x.AsOfDate)
                .ToListAsync(ct);

            var latest = snapshots
                .GroupBy(x => x.Symbol)
                .Select(g => g.First())
                .ToList();

            return Results.Ok(new
            {
                totalSnapshots = snapshots.Count,
                symbolsCovered = latest.Count,
                latestAsOfDate = latest.Count > 0 ? latest.Max(x => x.AsOfDate).ToString("yyyy-MM-dd") : null,
                coverage = new
                {
                    peRatio = latest.Count(x => x.PeRatio.HasValue),
                    pbRatio = latest.Count(x => x.PbRatio.HasValue),
                    roePercent = latest.Count(x => x.RoePercent.HasValue),
                    revenueGrowth = latest.Count(x => x.RevenueCurrent.HasValue && x.RevenuePrevious.HasValue),
                    netIncomeGrowth = latest.Count(x => x.NetIncomeCurrent.HasValue && x.NetIncomePrevious.HasValue),
                    turnaround = latest.Count(IsTurnaround)
                }
            });
        }).RequireAuthorization();

        group.MapGet("/financial-factors/query", async (
            decimal? peRatioMax,
            decimal? pbRatioMax,
            decimal? roePercentMin,
            decimal? operatingMarginMin,
            decimal? revenueGrowthMin,
            decimal? netIncomeGrowthMin,
            bool? turnaroundOnly,
            bool? positiveEarningsOnly,
            string? symbols,
            string? sectors,
            string? industries,
            string? search,
            int? limit,
            string? sortBy,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var symbolSet = ParseCsv(symbols);
            var sectorSet = ParseCsv(sectors);
            var industrySet = ParseCsv(industries);
            var requestedLimit = Math.Clamp(limit ?? (symbolSet.Count > 0 ? symbolSet.Count : 20), 1, 5000);

            var latestSnapshots = await db.FinancialSnapshots
                .AsNoTracking()
                .OrderByDescending(x => x.AsOfDate)
                .GroupBy(x => x.Symbol)
                .Select(g => g.First())
                .ToListAsync(ct);

            var tickers = await db.Tickers
                .AsNoTracking()
                .Where(t => t.IsActive)
                .ToDictionaryAsync(t => t.Symbol, ct);

            var rows = latestSnapshots
                .Select(snapshot =>
                {
                    tickers.TryGetValue(snapshot.Symbol, out var ticker);
                    return ToQueryRow(snapshot, ticker);
                });

            if (!string.IsNullOrWhiteSpace(search))
            {
                var normalized = search.Trim();
                rows = rows.Where(row =>
                    row.Symbol.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    row.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    row.Sector.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                    row.Industry.Contains(normalized, StringComparison.OrdinalIgnoreCase));
            }

            if (symbolSet.Count > 0)
                rows = rows.Where(row => symbolSet.Contains(row.Symbol.Trim(), StringComparer.OrdinalIgnoreCase));
            if (sectorSet.Count > 0)
                rows = rows.Where(row => sectorSet.Contains(row.Sector.Trim(), StringComparer.OrdinalIgnoreCase));
            if (industrySet.Count > 0)
                rows = rows.Where(row => industrySet.Contains(row.Industry.Trim(), StringComparer.OrdinalIgnoreCase));
            if (peRatioMax.HasValue)
                rows = rows.Where(row => row.PeRatio.HasValue && row.PeRatio <= peRatioMax.Value);
            if (pbRatioMax.HasValue)
                rows = rows.Where(row => row.PbRatio.HasValue && row.PbRatio <= pbRatioMax.Value);
            if (roePercentMin.HasValue)
                rows = rows.Where(row => row.RoePercent.HasValue && row.RoePercent >= roePercentMin.Value);
            if (operatingMarginMin.HasValue)
                rows = rows.Where(row => row.OperatingMarginPercent.HasValue && row.OperatingMarginPercent >= operatingMarginMin.Value);
            if (revenueGrowthMin.HasValue)
                rows = rows.Where(row => row.RevenueGrowthYoY.HasValue && row.RevenueGrowthYoY >= revenueGrowthMin.Value);
            if (netIncomeGrowthMin.HasValue)
                rows = rows.Where(row => row.NetIncomeGrowthYoY.HasValue && row.NetIncomeGrowthYoY >= netIncomeGrowthMin.Value);
            if (turnaroundOnly == true)
                rows = rows.Where(row => row.IsTurnaround);
            if (positiveEarningsOnly == true)
                rows = rows.Where(row => row.HasPositiveEarnings);

            var allRows = latestSnapshots
                .Select(snapshot =>
                {
                    tickers.TryGetValue(snapshot.Symbol, out var ticker);
                    return ToQueryRow(snapshot, ticker);
                })
                .ToList();

            rows = (sortBy ?? "peAsc").ToLowerInvariant() switch
            {
                "pbasc" => rows.OrderBy(row => row.PbRatio ?? decimal.MaxValue).ThenBy(row => row.Symbol),
                "roedesc" => rows.OrderByDescending(row => row.RoePercent ?? decimal.MinValue).ThenBy(row => row.Symbol),
                "revenuegrowthdesc" => rows.OrderByDescending(row => row.RevenueGrowthYoY ?? decimal.MinValue).ThenBy(row => row.Symbol),
                "netincomegrowthdesc" => rows.OrderByDescending(row => row.NetIncomeGrowthYoY ?? decimal.MinValue).ThenBy(row => row.Symbol),
                _ => rows.OrderBy(row => row.PeRatio ?? decimal.MaxValue).ThenBy(row => row.Symbol)
            };

            var matchedRows = rows.ToList();

            return Results.Ok(new
            {
                totalUniverse = allRows.Count,
                matched = matchedRows.Count,
                items = matchedRows.Take(requestedLimit).ToList(),
                comparison = new
                {
                    overall = BuildSummary(allRows),
                    filtered = BuildSummary(matchedRows)
                }
            });
        }).RequireAuthorization();

        group.MapPost("/financial-factors/import", async (
            List<FinancialSnapshotImportDto> items,
            AppDbContext db,
            Services.Financial.FinancialSnapshotImportService importService,
            CancellationToken ct) =>
        {
            if (items == null || items.Count == 0)
                return Results.BadRequest(new { error = "Import items are required." });

            var validCount = items.Count(item => !string.IsNullOrWhiteSpace(item.Symbol));
            if (validCount == 0)
                return Results.BadRequest(new { error = "At least one valid symbol is required." });

            var summary = await importService.UpsertAsync(db, items, ct);
            return Results.Ok(new { imported = summary.ImportedCount, skipped = summary.SkippedCount });
        }).RequireAuthorization();

        group.MapGet("/financial-factors/pipeline/status", async (
            AppDbContext db,
            BackgroundServices.FinancialSnapshotIngestionService pipeline,
            Services.Financial.SecFinancialSnapshotSyncService vendorSync,
            CancellationToken ct) =>
        {
            var recentRuns = await db.FinancialImportRuns
                .AsNoTracking()
                .OrderByDescending(run => run.StartedAt)
                .Take(10)
                .ToListAsync(ct);

            var latestSuccess = recentRuns.FirstOrDefault(run => run.Status == "Completed" && run.ImportedCount > 0)
                ?? await db.FinancialImportRuns.AsNoTracking()
                    .Where(run => run.Status == "Completed" && run.ImportedCount > 0)
                    .OrderByDescending(run => run.CompletedAt)
                    .FirstOrDefaultAsync(ct);

            return Results.Ok(new
            {
                enabled = pipeline.Enabled,
                importDirectory = pipeline.GetResolvedImportDirectory(),
                scanIntervalMinutes = pipeline.ScanIntervalMinutes,
                latestSuccessAt = latestSuccess?.CompletedAt?.ToString("o"),
                vendorSync = await vendorSync.GetStatusAsync(ct),
                recentRuns = recentRuns.Select(run => new
                {
                    run.Id,
                    run.SourceType,
                    run.FilePath,
                    run.Status,
                    run.ImportedCount,
                    run.SkippedCount,
                    run.ErrorMessage,
                    StartedAt = run.StartedAt.ToString("o"),
                    CompletedAt = run.CompletedAt?.ToString("o")
                })
            });
        }).RequireAuthorization();

        group.MapPost("/financial-factors/pipeline/run", async (
            BackgroundServices.FinancialSnapshotIngestionService pipeline,
            CancellationToken ct) =>
        {
            var summary = await pipeline.RunScanAsync(ct);
            return Results.Ok(summary);
        }).RequireAuthorization();

        group.MapPost("/financial-factors/vendor-sync/run", async (
            FinancialVendorSyncRequest? request,
            Services.Financial.SecFinancialSnapshotSyncService vendorSync,
            CancellationToken ct) =>
        {
            var symbols = ParseCsv(request?.Symbols).ToList();
            var summary = await vendorSync.RunSyncAsync(symbols, ct, force: true);
            return Results.Ok(summary);
        }).RequireAuthorization();

        return group;
    }

    private static object BuildSummary(List<FinancialFactorQueryRow> rows)
    {
        return new
        {
            count = rows.Count,
            averagePe = AverageNullable(rows.Select(x => x.PeRatio)),
            averagePb = AverageNullable(rows.Select(x => x.PbRatio)),
            averageRoe = AverageNullable(rows.Select(x => x.RoePercent)),
            averageRevenueGrowth = AverageNullable(rows.Select(x => x.RevenueGrowthYoY)),
            averageNetIncomeGrowth = AverageNullable(rows.Select(x => x.NetIncomeGrowthYoY)),
            positiveEarningsCount = rows.Count(x => x.HasPositiveEarnings),
            turnaroundCount = rows.Count(x => x.IsTurnaround)
        };
    }

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        var actual = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return actual.Count == 0 ? null : Math.Round(actual.Average(), 4);
    }

    private static HashSet<string> ParseCsv(string? raw)
    {
        return raw?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTurnaround(FinancialSnapshot snapshot)
    {
        return (snapshot.NetIncomePrevious.GetValueOrDefault() <= 0 && snapshot.NetIncomeCurrent.GetValueOrDefault() > 0)
            || (snapshot.OperatingIncomePrevious.GetValueOrDefault() <= 0 && snapshot.OperatingIncomeCurrent.GetValueOrDefault() > 0);
    }

    private static FinancialFactorQueryRow ToQueryRow(FinancialSnapshot snapshot, Ticker? ticker)
    {
        return new FinancialFactorQueryRow
        {
            Symbol = snapshot.Symbol,
            Name = ticker?.Name ?? string.Empty,
            Sector = ticker?.Sector ?? string.Empty,
            Industry = ticker?.Industry ?? string.Empty,
            MarketCap = ticker?.MarketCap,
            AsOfDate = snapshot.AsOfDate.ToString("yyyy-MM-dd"),
            PeRatio = snapshot.PeRatio,
            PbRatio = snapshot.PbRatio,
            RoePercent = snapshot.RoePercent,
            OperatingMarginPercent = snapshot.OperatingMarginPercent,
            RevenueGrowthYoY = ComputeGrowth(snapshot.RevenueCurrent, snapshot.RevenuePrevious),
            NetIncomeGrowthYoY = ComputeGrowth(snapshot.NetIncomeCurrent, snapshot.NetIncomePrevious),
            HasPositiveEarnings = snapshot.NetIncomeCurrent.GetValueOrDefault() > 0,
            IsTurnaround = IsTurnaround(snapshot),
            Source = snapshot.Source
        };
    }

    private static decimal? ComputeGrowth(decimal? current, decimal? previous)
    {
        if (!current.HasValue || !previous.HasValue || previous.Value == 0)
            return null;
        return Math.Round((current.Value - previous.Value) / Math.Abs(previous.Value), 4);
    }

    private sealed class FinancialFactorQueryRow
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public string Industry { get; set; } = string.Empty;
        public decimal? MarketCap { get; set; }
        public string AsOfDate { get; set; } = string.Empty;
        public decimal? PeRatio { get; set; }
        public decimal? PbRatio { get; set; }
        public decimal? RoePercent { get; set; }
        public decimal? OperatingMarginPercent { get; set; }
        public decimal? RevenueGrowthYoY { get; set; }
        public decimal? NetIncomeGrowthYoY { get; set; }
        public bool HasPositiveEarnings { get; set; }
        public bool IsTurnaround { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}

public class FinancialSnapshotImportDto
{
    public string? Symbol { get; set; }
    public DateTime? AsOfDate { get; set; }
    public string? Source { get; set; }
    public decimal? PeRatio { get; set; }
    public decimal? PbRatio { get; set; }
    public decimal? RoePercent { get; set; }
    public decimal? OperatingMarginPercent { get; set; }
    public decimal? RevenueCurrent { get; set; }
    public decimal? RevenuePrevious { get; set; }
    public decimal? OperatingIncomeCurrent { get; set; }
    public decimal? OperatingIncomePrevious { get; set; }
    public decimal? NetIncomeCurrent { get; set; }
    public decimal? NetIncomePrevious { get; set; }
    public string? Notes { get; set; }
}

public class FinancialVendorSyncRequest
{
    public string? Symbols { get; set; }
}
