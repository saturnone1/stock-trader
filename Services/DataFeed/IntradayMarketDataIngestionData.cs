using System.Threading.Channels;
using StockTrader.Application.MarketData;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.DataFeed;

/// <summary>설정, 선택된 공급자, SQLite 분봉, 스캐너 채널을 한 수집 세션으로 묶는 어댑터입니다.</summary>
public sealed class IntradayMarketDataIngestionData(
    IDataFeedServiceFactory dataFeeds,
    IOhlcvRepository bars,
    ISettingsRepository settings,
    Channel<string> symbolChannel) : IIntradayMarketDataIngestionData
{
    public async Task<IIntradayMarketDataIngestionSession> OpenSessionAsync(
        CancellationToken ct = default)
    {
        var userSettings = await settings.GetAsync(ct);
        var selection = await dataFeeds.SelectAsync(userSettings.PreferredDataSource, ct);
        return new Session(
            selection.Source,
            MarketSymbolPolicy.NormalizeMany(userSettings.WatchlistSymbols),
            selection.Service,
            bars,
            symbolChannel.Writer);
    }

    private sealed class Session(
        DataSource source,
        IReadOnlyList<string> watchlistSymbols,
        IDataFeedService feed,
        IOhlcvRepository bars,
        ChannelWriter<string> symbols) : IIntradayMarketDataIngestionSession
    {
        public DataSource Source { get; } = source;
        public IReadOnlyList<string> WatchlistSymbols { get; } = watchlistSymbols;

        public Task<OhlcvBar?> FetchLatestBarAsync(
            string symbol,
            CancellationToken ct = default) =>
            feed.GetLatestBarAsync(symbol, TimeFrame.OneMinute, ct);

        public Task SaveBarsAsync(
            IReadOnlyList<OhlcvBar> values,
            CancellationToken ct = default) =>
            bars.AddBarsAsync(values, ct);

        public async Task PublishIngestedSymbolsAsync(
            IReadOnlyList<string> values,
            CancellationToken ct = default)
        {
            foreach (var symbol in values)
                await symbols.WriteAsync(symbol, ct);
        }
    }
}
