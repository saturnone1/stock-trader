using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Application.MarketData;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Streaming;

namespace StockTrader.BackgroundServices;

public sealed class MarketDataSubscriptionSyncService(
    IServiceScopeFactory scopes,
    MarketDataServiceClient client,
    IStreamingStatusService streamingStatus,
    IOptions<MarketDataTransportOptions> transport,
    IOptions<StreamingSettings> streaming,
    TimeProvider timeProvider,
    ILogger<MarketDataSubscriptionSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (transport.Value.Mode != MarketDataTransportMode.Remote) return;
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(streaming.Value.WatchlistSyncIntervalSeconds), timeProvider);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var selection = await scope.ServiceProvider
                    .GetRequiredService<IRealtimeMarketDataSelectionReader>()
                    .ReadAsync(stoppingToken);
                var symbols = selection.Source == DataSource.Alpaca
                    ? selection.WatchlistSymbols
                    : [];
                var status = await client.SetSubscriptionsAsync(
                    new MarketDataSubscriptionRequest(
                        MarketDataContractVersions.Current,
                        DataSource.Alpaca.ToString(), symbols),
                    stoppingToken);
                if (status.StreamingConnected && symbols.Count > 0)
                    streamingStatus.MarkActive();
                else
                    streamingStatus.MarkInactive();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                streamingStatus.MarkReconnecting();
                logger.LogError(error, "Failed to synchronize Market Data streaming subscriptions.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class MarketDataShadowBackfillService(
    IServiceScopeFactory scopes,
    MarketDataServiceClient client,
    IOptions<MarketDataTransportOptions> transport,
    ILogger<MarketDataShadowBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = transport.Value;
        if (options.Mode != MarketDataTransportMode.Shadow || !options.ShadowBackfillEnabled)
            return;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await scope.ServiceProvider.GetRequiredService<ISettingsRepository>()
            .GetAsync(stoppingToken);
        var provider = settings.PreferredDataSource;
        var groups = await db.OhlcvBars.AsNoTracking()
            .GroupBy(bar => new { bar.Symbol, bar.TimeFrame })
            .Select(group => new
            {
                group.Key.Symbol,
                group.Key.TimeFrame,
                From = group.Min(bar => bar.Timestamp),
                To = group.Max(bar => bar.Timestamp)
            })
            .Take(options.ShadowBackfillMaxGroups)
            .ToListAsync(stoppingToken);

        foreach (var group in groups)
        {
            stoppingToken.ThrowIfCancellationRequested();
            var legacy = await db.OhlcvBars.AsNoTracking()
                .Where(bar => bar.Symbol == group.Symbol && bar.TimeFrame == group.TimeFrame)
                .OrderBy(bar => bar.Timestamp)
                .ToListAsync(stoppingToken);
            var remote = await client.HistoricalAsync(new MarketDataProviderRequest(
                MarketDataContractVersions.Current,
                provider.ToString(), group.Symbol, group.TimeFrame.ToString(),
                MarketDataContractHash.Utc(group.From),
                MarketDataContractHash.Utc(group.To),
                Persist: true), stoppingToken);
            var legacyHash = MarketDataContractHash.Content(
                legacy.Select(MarketDataContractMapper.ToContract));
            if (legacyHash == remote.Evidence.ContentHash)
                logger.LogInformation(
                    "Market Data shadow parity matched {Provider}/{Symbol}/{TimeFrame} ({Count} bars).",
                    provider, group.Symbol, group.TimeFrame, legacy.Count);
            else
                logger.LogWarning(
                    "Market Data shadow parity differed {Provider}/{Symbol}/{TimeFrame}: legacy={LegacyCount}/{LegacyHash}, remote={RemoteCount}/{RemoteHash}. Legacy provider identity was not inferable and was not imported.",
                    provider, group.Symbol, group.TimeFrame, legacy.Count, legacyHash,
                    remote.Bars.Count, remote.Evidence.ContentHash);
        }
    }
}
