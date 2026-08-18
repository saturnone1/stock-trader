namespace StockTrader.Application.Research;

public sealed record FinancialFactorCoverage(
    int PeRatio,
    int PbRatio,
    int RoePercent,
    int RevenueGrowth,
    int NetIncomeGrowth,
    int Turnaround);

public sealed record FinancialFactorMeta(
    int TotalSnapshots,
    int SymbolsCovered,
    DateTime? LatestAsOfDate,
    FinancialFactorCoverage Coverage);

public sealed record FinancialFactorQuery(
    decimal? PeRatioMax = null,
    decimal? PbRatioMax = null,
    decimal? RoePercentMin = null,
    decimal? OperatingMarginMin = null,
    decimal? RevenueGrowthMin = null,
    decimal? NetIncomeGrowthMin = null,
    bool? TurnaroundOnly = null,
    bool? PositiveEarningsOnly = null,
    string? Symbols = null,
    string? Sectors = null,
    string? Industries = null,
    string? Search = null,
    int? Limit = null,
    string? SortBy = null);

public sealed record FinancialFactorRow(
    string Symbol,
    string Name,
    string Sector,
    string Industry,
    decimal? MarketCap,
    DateTime AsOfDate,
    decimal? PeRatio,
    decimal? PbRatio,
    decimal? RoePercent,
    decimal? OperatingMarginPercent,
    decimal? RevenueGrowthYoY,
    decimal? NetIncomeGrowthYoY,
    bool HasPositiveEarnings,
    bool IsTurnaround,
    string Source);

public sealed record FinancialFactorSummary(
    int Count,
    decimal? AveragePe,
    decimal? AveragePb,
    decimal? AverageRoe,
    decimal? AverageRevenueGrowth,
    decimal? AverageNetIncomeGrowth,
    int PositiveEarningsCount,
    int TurnaroundCount);

public sealed record FinancialFactorComparison(
    FinancialFactorSummary Overall,
    FinancialFactorSummary Filtered);

public sealed record FinancialFactorResult(
    int TotalUniverse,
    int Matched,
    IReadOnlyList<FinancialFactorRow> Items,
    FinancialFactorComparison Comparison);

public sealed class FinancialFactorQueryService(IResearchUniverseStore store)
{
    public async Task<FinancialFactorMeta> GetMetaAsync(CancellationToken ct = default)
    {
        var data = await store.LoadFinancialResearchDataAsync(ct);
        var latest = SelectLatest(data.FinancialSnapshots);
        return new FinancialFactorMeta(
            data.FinancialSnapshots.Count,
            latest.Count,
            latest.Count > 0 ? latest.Max(snapshot => snapshot.AsOfDate) : null,
            new FinancialFactorCoverage(
                latest.Count(snapshot => snapshot.PeRatio.HasValue),
                latest.Count(snapshot => snapshot.PbRatio.HasValue),
                latest.Count(snapshot => snapshot.RoePercent.HasValue),
                latest.Count(snapshot => snapshot.RevenueCurrent.HasValue
                    && snapshot.RevenuePrevious.HasValue),
                latest.Count(snapshot => snapshot.NetIncomeCurrent.HasValue
                    && snapshot.NetIncomePrevious.HasValue),
                latest.Count(IsTurnaround)));
    }

    public async Task<FinancialFactorResult> QueryAsync(
        FinancialFactorQuery request,
        CancellationToken ct = default)
    {
        var symbolSet = ResearchFilterPolicy.ParseCsv(request.Symbols);
        var sectorSet = ResearchFilterPolicy.ParseCsv(request.Sectors);
        var industrySet = ResearchFilterPolicy.ParseCsv(request.Industries);
        var requestedLimit = Math.Clamp(
            request.Limit ?? (symbolSet.Count > 0
                ? symbolSet.Count
                : ResearchUniversePolicy.DefaultQueryLimit),
            1,
            ResearchUniversePolicy.MaximumFinancialFactorQueryLimit);
        var data = await store.LoadFinancialResearchDataAsync(ct);
        var tickerBySymbol = data.ActiveTickers.ToDictionary(
            ticker => ticker.Symbol,
            StringComparer.OrdinalIgnoreCase);
        var allRows = SelectLatest(data.FinancialSnapshots)
            .Select(snapshot => ToRow(
                snapshot,
                tickerBySymbol.GetValueOrDefault(snapshot.Symbol)))
            .ToArray();
        IEnumerable<FinancialFactorRow> rows = allRows;

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var normalized = request.Search.Trim();
            rows = rows.Where(row =>
                row.Symbol.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || row.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || row.Sector.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || row.Industry.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        }

        if (symbolSet.Count > 0)
            rows = rows.Where(row => symbolSet.Contains(row.Symbol.Trim()));
        if (sectorSet.Count > 0)
            rows = rows.Where(row => sectorSet.Contains(row.Sector.Trim()));
        if (industrySet.Count > 0)
            rows = rows.Where(row => industrySet.Contains(row.Industry.Trim()));
        if (request.PeRatioMax.HasValue)
            rows = rows.Where(row => row.PeRatio.HasValue && row.PeRatio <= request.PeRatioMax.Value);
        if (request.PbRatioMax.HasValue)
            rows = rows.Where(row => row.PbRatio.HasValue && row.PbRatio <= request.PbRatioMax.Value);
        if (request.RoePercentMin.HasValue)
            rows = rows.Where(row => row.RoePercent.HasValue && row.RoePercent >= request.RoePercentMin.Value);
        if (request.OperatingMarginMin.HasValue)
            rows = rows.Where(row => row.OperatingMarginPercent.HasValue
                && row.OperatingMarginPercent >= request.OperatingMarginMin.Value);
        if (request.RevenueGrowthMin.HasValue)
            rows = rows.Where(row => row.RevenueGrowthYoY.HasValue
                && row.RevenueGrowthYoY >= request.RevenueGrowthMin.Value);
        if (request.NetIncomeGrowthMin.HasValue)
            rows = rows.Where(row => row.NetIncomeGrowthYoY.HasValue
                && row.NetIncomeGrowthYoY >= request.NetIncomeGrowthMin.Value);
        if (request.TurnaroundOnly == true)
            rows = rows.Where(row => row.IsTurnaround);
        if (request.PositiveEarningsOnly == true)
            rows = rows.Where(row => row.HasPositiveEarnings);

        rows = (request.SortBy ?? "peAsc").ToLowerInvariant() switch
        {
            "pbasc" => rows
                .OrderBy(row => row.PbRatio ?? decimal.MaxValue)
                .ThenBy(row => row.Symbol),
            "roedesc" => rows
                .OrderByDescending(row => row.RoePercent ?? decimal.MinValue)
                .ThenBy(row => row.Symbol),
            "revenuegrowthdesc" => rows
                .OrderByDescending(row => row.RevenueGrowthYoY ?? decimal.MinValue)
                .ThenBy(row => row.Symbol),
            "netincomegrowthdesc" => rows
                .OrderByDescending(row => row.NetIncomeGrowthYoY ?? decimal.MinValue)
                .ThenBy(row => row.Symbol),
            _ => rows
                .OrderBy(row => row.PeRatio ?? decimal.MaxValue)
                .ThenBy(row => row.Symbol)
        };

        var matched = rows.ToArray();
        return new FinancialFactorResult(
            allRows.Length,
            matched.Length,
            matched.Take(requestedLimit).ToArray(),
            new FinancialFactorComparison(
                BuildSummary(allRows),
                BuildSummary(matched)));
    }

    public Task<FinancialImportRunHistory> GetImportRunHistoryAsync(
        int recentLimit,
        CancellationToken ct = default) =>
        store.LoadImportRunHistoryAsync(recentLimit, ct);

    private static IReadOnlyList<ResearchFinancialSnapshot> SelectLatest(
        IReadOnlyList<ResearchFinancialSnapshot> snapshots) =>
        snapshots
            .GroupBy(snapshot => snapshot.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(snapshot => snapshot.AsOfDate)
                .ThenByDescending(snapshot => snapshot.UpdatedAt)
                .ThenByDescending(snapshot => snapshot.SequenceId)
                .First())
            .ToArray();

    private static FinancialFactorRow ToRow(
        ResearchFinancialSnapshot snapshot,
        ResearchTickerSnapshot? ticker) => new(
        snapshot.Symbol,
        ticker?.Name ?? string.Empty,
        ticker?.Sector ?? string.Empty,
        ticker?.Industry ?? string.Empty,
        ticker?.MarketCap,
        snapshot.AsOfDate,
        snapshot.PeRatio,
        snapshot.PbRatio,
        snapshot.RoePercent,
        snapshot.OperatingMarginPercent,
        ComputeGrowth(snapshot.RevenueCurrent, snapshot.RevenuePrevious),
        ComputeGrowth(snapshot.NetIncomeCurrent, snapshot.NetIncomePrevious),
        snapshot.NetIncomeCurrent.GetValueOrDefault() > 0,
        IsTurnaround(snapshot),
        snapshot.Source);

    private static bool IsTurnaround(ResearchFinancialSnapshot snapshot) =>
        snapshot.NetIncomePrevious.GetValueOrDefault() <= 0
            && snapshot.NetIncomeCurrent.GetValueOrDefault() > 0
        || snapshot.OperatingIncomePrevious.GetValueOrDefault() <= 0
            && snapshot.OperatingIncomeCurrent.GetValueOrDefault() > 0;

    private static decimal? ComputeGrowth(decimal? current, decimal? previous)
    {
        if (!current.HasValue || !previous.HasValue || previous.Value == 0)
            return null;
        return Math.Round((current.Value - previous.Value) / Math.Abs(previous.Value), 4);
    }

    private static FinancialFactorSummary BuildSummary(
        IReadOnlyCollection<FinancialFactorRow> rows) => new(
        rows.Count,
        AverageNullable(rows.Select(row => row.PeRatio)),
        AverageNullable(rows.Select(row => row.PbRatio)),
        AverageNullable(rows.Select(row => row.RoePercent)),
        AverageNullable(rows.Select(row => row.RevenueGrowthYoY)),
        AverageNullable(rows.Select(row => row.NetIncomeGrowthYoY)),
        rows.Count(row => row.HasPositiveEarnings),
        rows.Count(row => row.IsTurnaround));

    private static decimal? AverageNullable(IEnumerable<decimal?> values)
    {
        var actual = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return actual.Length == 0 ? null : Math.Round(actual.Average(), 4);
    }
}
