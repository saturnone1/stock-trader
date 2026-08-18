using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.DataFeed;

/// <summary>현재 데이터 공급자 선택과 SQLite 일봉 조회를 연결하는 어댑터입니다.</summary>
public sealed class LiveDailyScanData(
    IDataFeedServiceFactory dataFeeds,
    IOhlcvRepository bars) : ILiveDailyScanData
{
    public async Task<LiveDailyScanContext> ResolveContextAsync(
        CancellationToken ct = default)
    {
        var selection = await dataFeeds.SelectAsync(null, ct);
        var provider = DataProviderCatalog.Get(selection.Source);
        return new LiveDailyScanContext(
            selection.Source,
            provider.MarketRegion,
            DataProviderCatalog.RegimeBenchmarkSymbol(selection.Source));
    }

    public async Task<IReadOnlyList<OhlcvBar>> LoadBarsAsync(
        string symbol,
        DateTime from,
        DateTime to,
        CancellationToken ct = default) =>
        await bars.GetBarsAsync(symbol, TimeFrame.Daily, from, to, ct);
}
