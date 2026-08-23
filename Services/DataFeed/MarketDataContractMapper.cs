using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.ServiceContracts.MarketData;

namespace StockTrader.Services.DataFeed;

internal static class MarketDataContractMapper
{
    public static MarketDataBar ToContract(OhlcvBar bar) => new(
        MarketSymbolPolicy.Normalize(bar.Symbol),
        bar.TimeFrame.ToString(),
        MarketDataContractHash.Utc(bar.Timestamp),
        bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Vwap);

    public static OhlcvBar ToModel(MarketDataBar bar) => new()
    {
        Symbol = MarketSymbolPolicy.Normalize(bar.Symbol),
        Timestamp = MarketDataContractHash.Utc(bar.TimestampUtc),
        TimeFrame = Enum.Parse<TimeFrame>(bar.TimeFrame, ignoreCase: true),
        Open = bar.Open,
        High = bar.High,
        Low = bar.Low,
        Close = bar.Close,
        Volume = bar.Volume,
        Vwap = bar.Vwap
    };

    public static MarketDataRangeRequest Range(
        DataSource provider,
        string symbol,
        TimeFrame frame,
        DateTime from,
        DateTime to)
    {
        var descriptor = DataProviderCatalog.Get(provider);
        return new MarketDataRangeRequest(
            MarketDataContractVersions.Current,
            provider.ToString(),
            MarketSymbolPolicy.Normalize(symbol),
            frame.ToString(),
            PriceAdjustmentCatalog.Resolve(provider, frame).ToString(),
            descriptor.Market,
            MarketCalendarVersion.Current,
            MarketDataContractHash.Utc(from),
            MarketDataContractHash.Utc(to));
    }
}
