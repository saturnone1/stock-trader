namespace StockTrader.Application.Research;

public sealed record ResearchTickerSnapshot(
    string Symbol,
    string Name,
    string Sector,
    string Industry,
    decimal MarketCap);

public sealed record ResearchFinancialSnapshot(
    long SequenceId,
    string Symbol,
    DateTime AsOfDate,
    string Source,
    decimal? PeRatio,
    decimal? PbRatio,
    decimal? RoePercent,
    decimal? OperatingMarginPercent,
    decimal? RevenueCurrent,
    decimal? RevenuePrevious,
    decimal? OperatingIncomeCurrent,
    decimal? OperatingIncomePrevious,
    decimal? NetIncomeCurrent,
    decimal? NetIncomePrevious,
    DateTime UpdatedAt);

public sealed record FinancialResearchDataSet(
    IReadOnlyList<ResearchFinancialSnapshot> FinancialSnapshots,
    IReadOnlyList<ResearchTickerSnapshot> ActiveTickers);

public sealed record FinancialImportRunSnapshot(
    long Id,
    string SourceType,
    string FilePath,
    string Status,
    int ImportedCount,
    int SkippedCount,
    string? ErrorMessage,
    DateTime StartedAt,
    DateTime? CompletedAt);

public sealed record FinancialImportRunHistory(
    IReadOnlyList<FinancialImportRunSnapshot> RecentRuns,
    FinancialImportRunSnapshot? LatestSuccessfulRun);

public sealed record FinancialSnapshotImportItem
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
}

public sealed record ManagedFinancialSnapshot(
    string Symbol,
    DateTime AsOfDate,
    string Source,
    decimal? PeRatio,
    decimal? PbRatio,
    decimal? RoePercent,
    decimal? OperatingMarginPercent,
    decimal? RevenueCurrent,
    decimal? RevenuePrevious,
    decimal? OperatingIncomeCurrent,
    decimal? OperatingIncomePrevious,
    decimal? NetIncomeCurrent,
    decimal? NetIncomePrevious,
    string? Notes,
    DateTime ModifiedAt);

public sealed record FinancialImportSummary(
    int ImportedCount = 0,
    int SkippedCount = 0);
