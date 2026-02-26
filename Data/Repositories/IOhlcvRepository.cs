using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Data.Repositories;

public interface IOhlcvRepository
{
    Task<List<OhlcvBar>> GetBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default);
    Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default);
    Task AddBarsAsync(IEnumerable<OhlcvBar> bars, CancellationToken ct = default);
    Task<DateTime?> GetLastTimestampAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default);
}
