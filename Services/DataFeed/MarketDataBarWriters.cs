using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Services.DataFeed;

public sealed class LocalMarketDataBarWriter(OhlcvRepository repository) : IMarketDataBarWriter
{
    public Task WriteAsync(MarketDataBarWrite write, CancellationToken ct = default) =>
        repository.AddBarsAsync(write.Bars, ct);
}

public sealed class RemoteMarketDataBarWriter(MarketDataServiceClient client) : IMarketDataBarWriter
{
    public async Task WriteAsync(MarketDataBarWrite write, CancellationToken ct = default)
    {
        if (write.Bars.Count == 0) return;
        var frames = write.Bars.Select(bar => bar.TimeFrame).Distinct().ToArray();
        if (frames.Length != 1)
            throw new ArgumentException("A Market Data write batch must contain one timeframe.", nameof(write));
        var frame = frames[0];
        var descriptor = DataProviderCatalog.Get(write.Provider);
        var bars = write.Bars.Select(MarketDataContractMapper.ToContract).ToArray();
        var requestId = write.RequestId ?? MarketDataContractHash.Sha256(string.Join('|',
            "application-write", write.Provider, MarketDataContractHash.Content(bars)));
        await client.UpsertAsync(new MarketDataUpsertRequest(
            MarketDataContractVersions.Current,
            requestId,
            write.Provider.ToString(),
            PriceAdjustmentCatalog.Resolve(write.Provider, frame).ToString(),
            descriptor.Market,
            MarketCalendarVersion.Current,
            write.RequestedFromUtc is { } from ? MarketDataContractHash.Utc(from) : null,
            write.RequestedToUtc is { } to ? MarketDataContractHash.Utc(to) : null,
            write.IsComplete,
            bars), ct);
    }
}

public sealed class MarketDataBarWriterRouter(
    LocalMarketDataBarWriter local,
    RemoteMarketDataBarWriter remote,
    IOptions<MarketDataTransportOptions> options,
    ILogger<MarketDataBarWriterRouter> logger) : IMarketDataBarWriter
{
    public async Task WriteAsync(MarketDataBarWrite write, CancellationToken ct = default)
    {
        switch (options.Value.Mode)
        {
            case MarketDataTransportMode.Local:
                await local.WriteAsync(write, ct);
                break;
            case MarketDataTransportMode.Remote:
                await remote.WriteAsync(write, ct);
                break;
            case MarketDataTransportMode.Shadow:
                await local.WriteAsync(write, ct);
                try
                {
                    await remote.WriteAsync(write, ct);
                }
                catch (Exception error)
                {
                    logger.LogError(error,
                        "Market Data shadow write failed for {Provider}; local authority was preserved.",
                        write.Provider);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Value.Mode));
        }
    }
}
