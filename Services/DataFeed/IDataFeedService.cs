using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

public interface IDataFeedService
{
    Task<List<OhlcvBar>> GetHistoricalBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default);
    Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default);
    Task<List<OhlcvBar>> GetIntradayBarsAsync(string symbol, DateTime date,
        CancellationToken ct = default);
    Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default);
}
