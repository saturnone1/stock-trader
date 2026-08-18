using StockTrader.Application.MarketData;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.Statistics;

namespace StockTrader.Services.DataFeed;

/// <summary>설정, 공급자, SQLite 일봉, 통계 갱신을 한 동기화 세션으로 묶는 어댑터입니다.</summary>
public sealed class DailyMarketDataSyncData(
    IDataFeedServiceFactory dataFeeds,
    IOhlcvRepository bars,
    ISettingsRepository settings,
    IStatisticsService statistics) : IDailyMarketDataSyncData
{
    public async Task<IDailyMarketDataSyncSession> OpenSessionAsync(
        CancellationToken ct = default)
    {
        var userSettings = await settings.GetAsync(ct);
        var selection = await dataFeeds.SelectAsync(userSettings.PreferredDataSource, ct);
        return new Session(
            selection.Source,
            MarketSymbolPolicy.NormalizeMany(userSettings.WatchlistSymbols),
            selection.Service,
            bars,
            statistics);
    }

    private sealed class Session(
        DataSource source,
        IReadOnlyList<string> watchlistSymbols,
        IDataFeedService feed,
        IOhlcvRepository bars,
        IStatisticsService statistics) : IDailyMarketDataSyncSession
    {
        public DataSource Source { get; } = source;
        public IReadOnlyList<string> WatchlistSymbols { get; } = watchlistSymbols;

        public async Task<IReadOnlyList<OhlcvBar>> LoadStoredBarsAsync(
            string symbol,
            DateTime from,
            DateTime to,
            CancellationToken ct = default) =>
            await bars.GetBarsAsync(symbol, TimeFrame.Daily, from, to, ct);

        public Task<DateTime?> GetLastStoredBarAsync(
            string symbol,
            CancellationToken ct = default) =>
            bars.GetLastTimestampAsync(symbol, TimeFrame.Daily, ct);

        public async Task<IReadOnlyList<OhlcvBar>> FetchBarsAsync(
            string symbol,
            DateTime from,
            DateTime to,
            CancellationToken ct = default) =>
            await feed.GetHistoricalBarsAsync(symbol, TimeFrame.Daily, from, to, ct);

        public Task SaveBarsAsync(
            IReadOnlyList<OhlcvBar> values,
            CancellationToken ct = default) =>
            bars.AddBarsAsync(values, ct);

        public Task RefreshStatisticsAsync(CancellationToken ct = default) =>
            statistics.RefreshAllStatsAsync(ct);
    }
}
