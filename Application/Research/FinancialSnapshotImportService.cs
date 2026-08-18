using StockTrader.Domain.MarketData;

namespace StockTrader.Application.Research;

public sealed class FinancialSnapshotImportService(
    IResearchUniverseStore store,
    TimeProvider timeProvider)
{
    public Task<FinancialImportSummary> UpsertAsync(
        IEnumerable<FinancialSnapshotImportItem> items,
        CancellationToken ct = default)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var normalized = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Symbol))
            .Select(item => new ManagedFinancialSnapshot(
                MarketSymbolPolicy.Normalize(item.Symbol),
                item.AsOfDate?.Date ?? nowUtc.Date,
                string.IsNullOrWhiteSpace(item.Source) ? "Manual" : item.Source.Trim(),
                item.PeRatio,
                item.PbRatio,
                item.RoePercent,
                item.OperatingMarginPercent,
                item.RevenueCurrent,
                item.RevenuePrevious,
                item.OperatingIncomeCurrent,
                item.OperatingIncomePrevious,
                item.NetIncomeCurrent,
                item.NetIncomePrevious,
                item.Notes?.Trim(),
                nowUtc))
            .GroupBy(item => (item.Symbol, item.AsOfDate))
            .Select(group => group.Last())
            .ToArray();
        return store.UpsertFinancialSnapshotsAsync(normalized, ct);
    }
}
