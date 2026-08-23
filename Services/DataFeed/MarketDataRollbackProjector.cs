using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Services.DataFeed;

public sealed class MarketDataRollbackProjector(
    MarketDataServiceClient client,
    OhlcvRepository local,
    ISettingsRepository settings,
    ILogger<MarketDataRollbackProjector> logger)
{
    public async Task ProjectAsync(CancellationToken ct)
    {
        var selected = (await settings.GetAsync(ct)).PreferredDataSource;
        var catalog = await client.SeriesAsync(ct);
        var series = catalog.Series
            .Where(item => string.Equals(item.Provider, selected.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (series.Length == 0)
            throw new InvalidOperationException(
                $"Market Data has no rollback series for selected provider {selected}.");

        foreach (var item in series)
        {
            var frame = Enum.Parse<TimeFrame>(item.TimeFrame, ignoreCase: true);
            var expectedAdjustment = PriceAdjustmentCatalog.Resolve(selected, frame).ToString();
            if (!string.Equals(item.AdjustmentMode, expectedAdjustment, StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Rollback series adjustment mismatch for {item.Symbol}/{frame}.");
            var request = MarketDataContractMapper.Range(
                selected, item.Symbol, frame, item.FirstBarUtc, item.LastBarUtc);
            var response = await client.ReadRangeAsync(request, ct);
            if (response.Bars.Count != item.BarCount)
                throw new InvalidDataException(
                    $"Rollback series count changed for {item.Symbol}/{frame}.");
            var bars = response.Bars.Select(MarketDataContractMapper.ToModel).ToArray();
            await local.AddBarsAsync(bars, ct);
            var projected = await local.GetBarsAsync(
                item.Symbol, frame, item.FirstBarUtc, item.LastBarUtc, ct);
            if (!MarketDataContractParity.ContentEquals(
                    response.Bars,
                    projected.Select(MarketDataContractMapper.ToContract)))
                throw new InvalidDataException(
                    $"Rollback projection content mismatch for {item.Symbol}/{frame}.");
            logger.LogInformation(
                "Projected Market Data rollback series {Provider}/{Symbol}/{TimeFrame}: {Count} bars.",
                selected, item.Symbol, frame, bars.Length);
        }
    }
}
