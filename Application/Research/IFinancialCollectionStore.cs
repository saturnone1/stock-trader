namespace StockTrader.Application.Research;

public interface IFinancialCollectionStore
{
    Task<bool> HasCompletedRunAsync(
        string filePath,
        string fingerprint,
        CancellationToken ct = default);

    Task<long> StartOrRestartRunAsync(
        string sourceType,
        string filePath,
        string fingerprint,
        DateTime startedAt,
        CancellationToken ct = default);

    Task CompleteRunAsync(
        long runId,
        int importedCount,
        int skippedCount,
        string? warning,
        DateTime completedAt,
        CancellationToken ct = default);

    Task FailRunAsync(
        long runId,
        string error,
        DateTime completedAt,
        CancellationToken ct = default);

    Task<DateTime?> GetLatestCompletedAtAsync(
        string sourceType,
        bool requireImportedItems,
        CancellationToken ct = default);

    Task<IReadOnlyList<ResearchTickerSnapshot>> LoadTopActiveTickersAsync(
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ResearchTickerSnapshot>> LoadTickersAsync(
        IReadOnlyCollection<string> symbols,
        CancellationToken ct = default);
}
