namespace StockTrader.Application.Research;

public interface IResearchUniverseStore
{
    Task<IReadOnlyList<ResearchTickerSnapshot>> LoadActiveTickersAsync(
        CancellationToken ct = default);

    Task<FinancialResearchDataSet> LoadFinancialResearchDataAsync(
        CancellationToken ct = default);

    Task<FinancialImportRunHistory> LoadImportRunHistoryAsync(
        int recentLimit,
        CancellationToken ct = default);

    Task<FinancialImportSummary> UpsertFinancialSnapshotsAsync(
        IReadOnlyList<ManagedFinancialSnapshot> snapshots,
        CancellationToken ct = default);
}
