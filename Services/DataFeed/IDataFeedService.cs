using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

public interface IDataFeedService
{
    /// <summary>
    /// 이 어댑터가 대표하는 공급자. 준비 단계가 시장·시간대·조정 모드 근거를
    /// 카탈로그에서 조립하려면 어떤 공급자가 봉을 제공했는지 알아야 한다.
    /// </summary>
    DataSource Source { get; }

    Task<List<OhlcvBar>> GetHistoricalBarsAsync(string symbol, TimeFrame timeFrame,
        DateTime from, DateTime to, CancellationToken ct = default);
    Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame,
        CancellationToken ct = default);
    Task<List<OhlcvBar>> GetIntradayBarsAsync(string symbol, DateTime date,
        CancellationToken ct = default);
    Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default);
}
