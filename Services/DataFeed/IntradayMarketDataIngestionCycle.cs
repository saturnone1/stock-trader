using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;

namespace StockTrader.Services.DataFeed;

/// <summary>선택된 공급자가 소유한 시장의 정규장에만 최신 분봉을 수집합니다.</summary>
public sealed class IntradayMarketDataIngestionCycle(
    IIntradayMarketDataIngestionData data,
    IRealtimeMarketDataStatus realtimeStatus,
    IMarketCalendar marketCalendar,
    ILogger<IntradayMarketDataIngestionCycle> logger) : IIntradayMarketDataIngestionCycle
{
    public async Task<IntradayMarketDataIngestionResult> RunAsync(
        CancellationToken ct = default)
    {
        var session = await data.OpenSessionAsync(ct);
        var activeRealtimeSource = realtimeStatus.ActiveSource;
        if (activeRealtimeSource == session.Source)
        {
            return new IntradayMarketDataIngestionResult(
                IntradayMarketDataIngestionStatus.RealtimeStreamActive,
                session.Source);
        }
        var connectedRealtimeSource = realtimeStatus.ConnectedSource;
        if (connectedRealtimeSource is not null
            && connectedRealtimeSource != session.Source)
        {
            logger.LogWarning(
                "Waiting for realtime provider {ConnectedSource} to stop before polling {SelectedSource}",
                connectedRealtimeSource,
                session.Source);
            return new IntradayMarketDataIngestionResult(
                IntradayMarketDataIngestionStatus.RealtimeProviderTransition,
                session.Source);
        }

        var market = DataProviderCatalog.Get(session.Source).MarketRegion;
        if (!marketCalendar.IsMarketOpen(market))
        {
            return new IntradayMarketDataIngestionResult(
                IntradayMarketDataIngestionStatus.MarketClosed,
                session.Source);
        }

        if (session.WatchlistSymbols.Count == 0)
        {
            return new IntradayMarketDataIngestionResult(
                IntradayMarketDataIngestionStatus.NoSymbols,
                session.Source);
        }

        var batch = new List<Models.OhlcvBar>(session.WatchlistSymbols.Count);
        var successfulSymbols = new List<string>(session.WatchlistSymbols.Count);
        var errors = 0;
        Exception? firstError = null;
        foreach (var symbol in session.WatchlistSymbols)
        {
            try
            {
                var bar = await session.FetchLatestBarAsync(symbol, ct);
                if (bar is null)
                    continue;

                batch.Add(bar);
                successfulSymbols.Add(symbol);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors++;
                firstError ??= exception;
                logger.LogError(exception, "Error ingesting data for {Symbol}", symbol);
            }
        }

        if (errors == session.WatchlistSymbols.Count)
        {
            throw new InvalidOperationException(
                $"Latest-bar ingestion failed for all {errors} symbols from {session.Source}.",
                firstError);
        }

        if (batch.Count > 0)
        {
            await session.SaveBarsAsync(batch, ct);
            await session.PublishIngestedSymbolsAsync(successfulSymbols, ct);
        }

        logger.LogInformation(
            "Ingestion cycle complete: {Ingested}/{Total} symbols ingested, {Errors} errors",
            successfulSymbols.Count,
            session.WatchlistSymbols.Count,
            errors);
        return new IntradayMarketDataIngestionResult(
            errors == 0
                ? IntradayMarketDataIngestionStatus.Completed
                : IntradayMarketDataIngestionStatus.PartiallyFailed,
            session.Source,
            session.WatchlistSymbols.Count,
            successfulSymbols.Count,
            errors);
    }
}
