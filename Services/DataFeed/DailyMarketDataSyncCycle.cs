using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;

namespace StockTrader.Services.DataFeed;

/// <summary>선택된 공급자의 시장일을 기준으로 일봉 이력을 복구하고 동기화합니다.</summary>
public sealed class DailyMarketDataSyncCycle(
    IDailyMarketDataSyncData data,
    IMarketCalendar marketCalendar,
    DailyMarketDataSyncState state,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<DailyMarketDataSyncCycle> logger) : IDailyMarketDataSyncCycle
{
    public async Task RunInitialSyncIfNeededAsync(CancellationToken ct = default)
    {
        try
        {
            var session = await data.OpenSessionAsync(ct);
            var observedAt = timeProvider.GetUtcNow().UtcDateTime;
            var window = ResolveWindow(session.Source, observedAt);
            var symbols = DailyMarketDataSyncPolicy.ResolveRequiredSymbols(
                session.WatchlistSymbols,
                session.Source);
            var from = observedAt.AddDays(
                -StrategyEvaluationPolicy.RegimeLookbackCalendarDays);
            var needingSync = new List<string>();

            foreach (var symbol in symbols)
            {
                var stored = await session.LoadStoredBarsAsync(symbol, from, observedAt, ct);
                var completedCount = stored.Count(bar =>
                    DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(bar.Timestamp, window));
                if (completedCount < DailyMarketDataSyncPolicy.MinimumRequiredBars(
                        symbol, session.Source))
                    needingSync.Add(symbol);
            }

            if (needingSync.Count == 0)
            {
                logger.LogInformation(
                    "Initial sync: all {Count} symbols have sufficient daily bars",
                    symbols.Count);
                return;
            }

            logger.LogInformation(
                "Initial sync: {NeedSync}/{Total} symbols need daily bars — syncing now: {Symbols}",
                needingSync.Count,
                symbols.Count,
                string.Join(", ", needingSync));
            var synced = 0;
            foreach (var symbol in needingSync)
            {
                try
                {
                    var fetched = (await session.FetchBarsAsync(symbol, from, observedAt, ct))
                        .Where(bar => DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(
                            bar.Timestamp, window))
                        .ToArray();
                    if (fetched.Length == 0)
                        continue;

                    await session.SaveBarsAsync(fetched, ct);
                    synced++;
                    logger.LogDebug(
                        "Initial sync: {Count} daily bars for {Symbol}",
                        fetched.Length,
                        symbol);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Initial sync failed for {Symbol} — will retry at regular sync",
                        symbol);
                }
            }

            logger.LogInformation(
                "Initial sync complete: {Synced}/{NeedSync} symbols synced",
                synced,
                needingSync.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Initial sync check failed — scanner will use available data");
        }
    }

    public async Task<DailyMarketDataSyncResult> RunScheduledAsync(
        CancellationToken ct = default)
    {
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var session = await data.OpenSessionAsync(ct);
        var window = ResolveWindow(session.Source, observedAt);
        if (!window.IsReady)
            return new DailyMarketDataSyncResult(DailyMarketDataSyncStatus.NotReady);
        if (state.WasCompleted(session.Source, window.MarketDate))
            return new DailyMarketDataSyncResult(DailyMarketDataSyncStatus.AlreadyCompleted);

        var symbols = DailyMarketDataSyncPolicy.ResolveRequiredSymbols(
            session.WatchlistSymbols,
            session.Source);
        var synced = 0;
        var totalBars = 0;
        var errors = 0;
        foreach (var symbol in symbols)
        {
            try
            {
                var last = await session.GetLastStoredBarAsync(symbol, ct);
                var from = last?.Date
                    ?? observedAt.AddYears(-DailyMarketDataSyncPolicy.BootstrapLookbackYears);
                var fetched = (await session.FetchBarsAsync(symbol, from, observedAt, ct))
                    .Where(bar => DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(
                        bar.Timestamp, window))
                    .ToArray();
                if (fetched.Length == 0)
                    continue;

                await session.SaveBarsAsync(fetched, ct);
                synced++;
                totalBars += fetched.Length;
                logger.LogDebug(
                    "Synced {Count} daily bars for {Symbol}",
                    fetched.Length,
                    symbol);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors++;
                logger.LogError(exception, "Error syncing daily data for {Symbol}", symbol);
            }
        }

        try
        {
            await session.RefreshStatisticsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "통계 갱신 중 오류 (데이터 동기화는 완료됨)");
        }

        logger.LogInformation(
            "Daily sync complete: {Synced}/{Total} symbols, {Bars} bars synced, {Errors} errors",
            synced,
            symbols.Count,
            totalBars,
            errors);
        if (errors == 0)
        {
            state.MarkCompleted(session.Source, window.MarketDate);
            return new DailyMarketDataSyncResult(
                DailyMarketDataSyncStatus.Completed,
                symbols.Count,
                synced,
                totalBars);
        }

        logger.LogWarning(
            "Partial sync: {Errors} symbols failed, will retry next cycle",
            errors);
        return new DailyMarketDataSyncResult(
            DailyMarketDataSyncStatus.PartiallyFailed,
            symbols.Count,
            synced,
            totalBars,
            errors);
    }

    private DailyMarketDataSyncWindow ResolveWindow(
        DataSource source,
        DateTime observedAt)
    {
        var market = DataProviderCatalog.Get(source).MarketRegion;
        return DailyMarketDataSyncPolicy.EvaluateWindow(
            marketCalendar.GetLocalTime(market, observedAt),
            marketCalendar.GetMarketClose(market),
            TimeSpan.FromMinutes(settings.Value.DailyDataSyncCloseDelayMinutes));
    }
}
