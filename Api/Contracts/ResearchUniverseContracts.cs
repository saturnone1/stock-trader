using StockTrader.Application.Research;
using StockTrader.Services.Financial;

namespace StockTrader.Api.Contracts;

public sealed record ResearchFacetResponse(string Name, int Count)
{
    public static ResearchFacetResponse Create(ResearchFacet value) => new(value.Name, value.Count);
}

public sealed record ResearchUniverseMetaResponse(
    int TotalActive,
    int MarketCapCoverage,
    IReadOnlyList<ResearchFacetResponse> Sectors,
    IReadOnlyList<ResearchFacetResponse> Industries)
{
    public static ResearchUniverseMetaResponse Create(ResearchUniverseMeta value) => new(
        value.TotalActive,
        value.MarketCapCoverage,
        value.Sectors.Select(ResearchFacetResponse.Create).ToArray(),
        value.Industries.Select(ResearchFacetResponse.Create).ToArray());
}

public sealed record ResearchUniverseRowResponse(
    string Symbol,
    string Name,
    string Sector,
    string Industry,
    decimal MarketCap,
    decimal MarketCapPercentile)
{
    public static ResearchUniverseRowResponse Create(ResearchUniverseRow value) => new(
        value.Symbol,
        value.Name,
        value.Sector,
        value.Industry,
        value.MarketCap,
        value.MarketCapPercentile);
}

public sealed record ResearchUniverseQueryResponse(
    int TotalUniverse,
    int Matched,
    IReadOnlyList<ResearchUniverseRowResponse> Items)
{
    public static ResearchUniverseQueryResponse Create(ResearchUniverseResult value) => new(
        value.TotalUniverse,
        value.Matched,
        value.Items.Select(ResearchUniverseRowResponse.Create).ToArray());
}

public sealed record FinancialFactorCoverageResponse(
    int PeRatio,
    int PbRatio,
    int RoePercent,
    int RevenueGrowth,
    int NetIncomeGrowth,
    int Turnaround)
{
    public static FinancialFactorCoverageResponse Create(FinancialFactorCoverage value) => new(
        value.PeRatio,
        value.PbRatio,
        value.RoePercent,
        value.RevenueGrowth,
        value.NetIncomeGrowth,
        value.Turnaround);
}

public sealed record FinancialFactorMetaResponse(
    int TotalSnapshots,
    int SymbolsCovered,
    string? LatestAsOfDate,
    FinancialFactorCoverageResponse Coverage)
{
    public static FinancialFactorMetaResponse Create(FinancialFactorMeta value) => new(
        value.TotalSnapshots,
        value.SymbolsCovered,
        value.LatestAsOfDate?.ToString("yyyy-MM-dd"),
        FinancialFactorCoverageResponse.Create(value.Coverage));
}

public sealed record FinancialFactorRowResponse(
    string Symbol,
    string Name,
    string Sector,
    string Industry,
    decimal? MarketCap,
    string AsOfDate,
    decimal? PeRatio,
    decimal? PbRatio,
    decimal? RoePercent,
    decimal? OperatingMarginPercent,
    decimal? RevenueGrowthYoY,
    decimal? NetIncomeGrowthYoY,
    bool HasPositiveEarnings,
    bool IsTurnaround,
    string Source)
{
    public static FinancialFactorRowResponse Create(FinancialFactorRow value) => new(
        value.Symbol,
        value.Name,
        value.Sector,
        value.Industry,
        value.MarketCap,
        value.AsOfDate.ToString("yyyy-MM-dd"),
        value.PeRatio,
        value.PbRatio,
        value.RoePercent,
        value.OperatingMarginPercent,
        value.RevenueGrowthYoY,
        value.NetIncomeGrowthYoY,
        value.HasPositiveEarnings,
        value.IsTurnaround,
        value.Source);
}

public sealed record FinancialFactorSummaryResponse(
    int Count,
    decimal? AveragePe,
    decimal? AveragePb,
    decimal? AverageRoe,
    decimal? AverageRevenueGrowth,
    decimal? AverageNetIncomeGrowth,
    int PositiveEarningsCount,
    int TurnaroundCount)
{
    public static FinancialFactorSummaryResponse Create(FinancialFactorSummary value) => new(
        value.Count,
        value.AveragePe,
        value.AveragePb,
        value.AverageRoe,
        value.AverageRevenueGrowth,
        value.AverageNetIncomeGrowth,
        value.PositiveEarningsCount,
        value.TurnaroundCount);
}

public sealed record FinancialFactorComparisonResponse(
    FinancialFactorSummaryResponse Overall,
    FinancialFactorSummaryResponse Filtered);

public sealed record FinancialFactorQueryResponse(
    int TotalUniverse,
    int Matched,
    IReadOnlyList<FinancialFactorRowResponse> Items,
    FinancialFactorComparisonResponse Comparison)
{
    public static FinancialFactorQueryResponse Create(FinancialFactorResult value) => new(
        value.TotalUniverse,
        value.Matched,
        value.Items.Select(FinancialFactorRowResponse.Create).ToArray(),
        new FinancialFactorComparisonResponse(
            FinancialFactorSummaryResponse.Create(value.Comparison.Overall),
            FinancialFactorSummaryResponse.Create(value.Comparison.Filtered)));
}

public sealed record FinancialSnapshotImportDto
{
    public string? Symbol { get; init; }
    public DateTime? AsOfDate { get; init; }
    public string? Source { get; init; }
    public decimal? PeRatio { get; init; }
    public decimal? PbRatio { get; init; }
    public decimal? RoePercent { get; init; }
    public decimal? OperatingMarginPercent { get; init; }
    public decimal? RevenueCurrent { get; init; }
    public decimal? RevenuePrevious { get; init; }
    public decimal? OperatingIncomeCurrent { get; init; }
    public decimal? OperatingIncomePrevious { get; init; }
    public decimal? NetIncomeCurrent { get; init; }
    public decimal? NetIncomePrevious { get; init; }
    public string? Notes { get; init; }

    public FinancialSnapshotImportItem ToItem() => new()
    {
        Symbol = Symbol,
        AsOfDate = AsOfDate,
        Source = Source,
        PeRatio = PeRatio,
        PbRatio = PbRatio,
        RoePercent = RoePercent,
        OperatingMarginPercent = OperatingMarginPercent,
        RevenueCurrent = RevenueCurrent,
        RevenuePrevious = RevenuePrevious,
        OperatingIncomeCurrent = OperatingIncomeCurrent,
        OperatingIncomePrevious = OperatingIncomePrevious,
        NetIncomeCurrent = NetIncomeCurrent,
        NetIncomePrevious = NetIncomePrevious,
        Notes = Notes
    };
}

public sealed record FinancialVendorSyncRequest(string? Symbols);
public sealed record FinancialImportResponse(int Imported, int Skipped);
public sealed record FinancialImportErrorResponse(string Error);

public sealed record FinancialImportRunResponse(
    long Id,
    string SourceType,
    string FilePath,
    string Status,
    int ImportedCount,
    int SkippedCount,
    string? ErrorMessage,
    string StartedAt,
    string? CompletedAt)
{
    public static FinancialImportRunResponse Create(FinancialImportRunSnapshot value) => new(
        value.Id,
        value.SourceType,
        value.FilePath,
        value.Status,
        value.ImportedCount,
        value.SkippedCount,
        value.ErrorMessage,
        value.StartedAt.ToString("o"),
        value.CompletedAt?.ToString("o"));
}

public sealed record FinancialVendorSyncStatusResponse(
    bool Enabled,
    string Provider,
    int SyncIntervalHours,
    int SymbolLimit,
    int ConfiguredSymbolCount,
    IReadOnlyList<string> ConfiguredSymbols,
    string? LatestSuccessAt)
{
    public static FinancialVendorSyncStatusResponse Create(FinancialVendorSyncStatus value) => new(
        value.Enabled,
        value.Provider,
        value.SyncIntervalHours,
        value.SymbolLimit,
        value.ConfiguredSymbolCount,
        value.ConfiguredSymbols,
        value.LatestSuccessAt?.ToString("o"));
}

public sealed record FinancialPipelineStatusResponse(
    bool Enabled,
    string ImportDirectory,
    int ScanIntervalMinutes,
    string? LatestSuccessAt,
    FinancialVendorSyncStatusResponse VendorSync,
    IReadOnlyList<FinancialImportRunResponse> RecentRuns);

public sealed record FinancialPipelineRunResponse(
    string Status,
    string Message,
    int ImportedCount,
    int SkippedCount,
    int ProcessedFiles)
{
    public static FinancialPipelineRunResponse Create(FinancialPipelineRunSummary value) => new(
        value.Status,
        value.Message,
        value.ImportedCount,
        value.SkippedCount,
        value.ProcessedFiles);
}
