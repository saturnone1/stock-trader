using Alpaca.Markets;
using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Notification;
using StockTrader.Services.Streaming;
using TimeFrame = StockTrader.Domain.MarketData.TimeFrame;
using DataSource = StockTrader.Domain.MarketData.DataSource;

namespace StockTrader.BackgroundServices;

public class AlpacaStreamingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRealtimeBarIngestionBuffer _barIngestion;
    private readonly AlpacaSettings _settings;
    private readonly StreamingSettings _streamingSettings;
    private readonly TimeProvider _timeProvider;
    private readonly IStreamingStatusService _streamingStatus;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AlpacaStreamingService> _logger;

    private readonly HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    // BUG-C05 fix: store subscription objects keyed by symbol so unsubscribe reuses the same instance
    private readonly Dictionary<string, IAlpacaDataSubscription<IBar>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    // BUG-C05 fix: store named handlers so -= removes the exact same delegate reference
    private readonly Dictionary<string, Action<IBar>> _barHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _symbolsLock = new();

    public AlpacaStreamingService(
        IServiceScopeFactory scopeFactory,
        IRealtimeBarIngestionBuffer barIngestion,
        IOptions<AlpacaSettings> settings,
        IOptions<StreamingSettings> streamingSettings,
        TimeProvider timeProvider,
        IStreamingStatusService streamingStatus,
        INotificationService notificationService,
        ILogger<AlpacaStreamingService> logger)
    {
        _scopeFactory = scopeFactory;
        _barIngestion = barIngestion;
        _settings = settings.Value;
        _streamingSettings = streamingSettings.Value;
        _timeProvider = timeProvider;
        _streamingStatus = streamingStatus;
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.HasConfiguredCredentials)
        {
            _logger.LogWarning("AlpacaStreamingService disabled (credentials not configured)");
            return;
        }

        if (!_settings.EnableStreaming)
        {
            _logger.LogInformation("AlpacaStreamingService disabled (EnableStreaming=false)");
            return;
        }

        _logger.LogInformation("AlpacaStreamingService starting");

        // Start the bar-flush loop as a concurrent background task
        using var flushCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var flushTask = _barIngestion.RunFlushLoopAsync(flushCancellation.Token);

        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await IsAlpacaSelectedAsync(stoppingToken))
                {
                    if (await _barIngestion.FlushAsync(stoppingToken))
                        _streamingStatus.MarkInactive();
                    await Task.Delay(
                        TimeSpan.FromSeconds(_streamingSettings.WatchlistSyncIntervalSeconds),
                        _timeProvider,
                        stoppingToken);
                    continue;
                }

                await ConnectAndStreamAsync(stoppingToken);
                if (await _barIngestion.FlushAsync(stoppingToken))
                {
                    _streamingStatus.MarkInactive();
                    _notificationService.PublishStreamingStatus(false);
                }
                attempt = 0; // reset on clean disconnect
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                attempt++;
                _streamingStatus.MarkInactive();
                _notificationService.PublishStreamingStatus(false);

                if (attempt > _streamingSettings.MaxReconnectAttempts)
                {
                    _logger.LogWarning(
                        "Max reconnect attempts ({Max}) exceeded. Falling back to polling",
                        _streamingSettings.MaxReconnectAttempts);
                    break;
                }

                var delay = CalculateBackoffDelay(attempt);
                _logger.LogWarning(ex,
                    "Streaming connection lost (attempt {Attempt}/{Max}). Reconnecting in {Delay}s",
                    attempt, _streamingSettings.MaxReconnectAttempts, delay.TotalSeconds);

                await Task.Delay(delay, _timeProvider, stoppingToken);
            }
        }

        // Signal the flush loop to drain remaining bars, then wait for it to finish
        _barIngestion.Complete();
        flushCancellation.Cancel();
        await flushTask;
    }

    private async Task ConnectAndStreamAsync(CancellationToken ct)
    {
        var secretKey = new SecretKey(_settings.ApiKey, _settings.ApiSecret);

        using var client = _settings.IsPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaDataStreamingClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaDataStreamingClient(secretKey);

        // BUG-C06 fix: cancel the sync loop immediately when the WebSocket drops
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        client.SocketClosed += () =>
        {
            _logger.LogWarning("Alpaca WebSocket closed unexpectedly — triggering reconnect");
            _streamingStatus.MarkInactive();
            _notificationService.PublishStreamingStatus(false);
            _barIngestion.RejectNewBars();
            // Cancelling cts unblocks WatchlistSyncLoopAsync, which causes
            // ConnectAndStreamAsync to throw IOException below, and the
            // ExecuteAsync backoff loop handles exponential-delay reconnection.
            cts.Cancel();
        };

        var authStatus = await client.ConnectAndAuthenticateAsync(ct);
        if (authStatus != AuthStatus.Authorized)
        {
            throw new InvalidOperationException($"Alpaca streaming auth failed: {authStatus}");
        }

        _logger.LogInformation("Alpaca WebSocket streaming connected and authenticated");

        // Initial subscription
        var selection = await GetRuntimeSelectionAsync(ct);
        if (selection.Source != DataSource.Alpaca)
            return;

        _barIngestion.StartAccepting();
        _streamingStatus.MarkConnected();
        if (selection.WatchlistSymbols.Count > 0)
        {
            await SubscribeToSymbolsAsync(client, selection.WatchlistSymbols, cts.Token);
        }

        _notificationService.PublishStreamingStatus(true);

        // Run watchlist sync loop — exits when ct (app stop) or cts (socket closed) is cancelled.
        // Clear _subscribedSymbols only AFTER the sync loop has fully exited to avoid a race
        // where SocketClosed fires while WatchlistSyncLoopAsync is still iterating the set.
        try
        {
            await WatchlistSyncLoopAsync(client, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // SocketClosed triggered cts.Cancel(); surface as a connection failure
            // so the outer backoff loop retries.
            throw new IOException("WebSocket connection closed — scheduling reconnect");
        }
        finally
        {
            await _barIngestion.StopAcceptingAsync();
            // Safe to clear now: sync loop has exited and no more bar callbacks can update these
            lock (_symbolsLock)
            {
                _subscribedSymbols.Clear();
                _subscriptions.Clear();
                _barHandlers.Clear();
            }
        }
    }

    private async Task WatchlistSyncLoopAsync(IAlpacaDataStreamingClient client, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_streamingSettings.WatchlistSyncIntervalSeconds),
            _timeProvider);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var selection = await GetRuntimeSelectionAsync(ct);
                if (selection.Source != DataSource.Alpaca)
                {
                    _logger.LogInformation(
                        "Alpaca streaming stopping because selected provider changed to {Source}",
                        selection.Source);
                    return;
                }

                var currentSet = new HashSet<string>(
                    selection.WatchlistSymbols,
                    StringComparer.OrdinalIgnoreCase);

                List<string> toSubscribe;
                List<string> toUnsubscribe;
                lock (_symbolsLock)
                {
                    toSubscribe = currentSet.Except(_subscribedSymbols, StringComparer.OrdinalIgnoreCase).ToList();
                    toUnsubscribe = _subscribedSymbols.Except(currentSet, StringComparer.OrdinalIgnoreCase).ToList();
                }

                if (toSubscribe.Count > 0)
                {
                    await SubscribeToSymbolsAsync(client, toSubscribe, ct);
                }
                if (toUnsubscribe.Count > 0)
                {
                    await UnsubscribeFromSymbolsAsync(client, toUnsubscribe, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during watchlist sync");
            }
        }
    }

    private async Task SubscribeToSymbolsAsync(
        IAlpacaDataStreamingClient client, IReadOnlyList<string> symbols, CancellationToken ct)
    {
        foreach (var symbol in symbols)
        {
            var subscription = client.GetMinuteBarSubscription(symbol);

            // BUG-C05 fix: store the handler instance so unsubscribe can remove the exact delegate.
            // Wrap in Task.Run + try-catch so exceptions are logged rather than silently lost.
            var capturedSymbol = symbol;
            Action<IBar> handler = bar =>
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _barIngestion.ProcessAsync(new OhlcvBar
                        {
                            Symbol = capturedSymbol,
                            Timestamp = bar.TimeUtc,
                            TimeFrame = TimeFrame.OneMinute,
                            Open = bar.Open,
                            High = bar.High,
                            Low = bar.Low,
                            Close = bar.Close,
                            Volume = (long)bar.Volume,
                            Vwap = bar.Vwap
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing bar for {Symbol}", capturedSymbol);
                    }
                });
            };
            subscription.Received += handler;

            await client.SubscribeAsync(subscription, ct);

            lock (_symbolsLock)
            {
                _subscribedSymbols.Add(symbol);
                _subscriptions[symbol] = subscription;
                _barHandlers[symbol] = handler;
            }
        }

        _logger.LogInformation("Subscribed to streaming for {Count} symbols: {Symbols}",
            symbols.Count, string.Join(", ", symbols));
    }

    private async Task UnsubscribeFromSymbolsAsync(
        IAlpacaDataStreamingClient client, IReadOnlyList<string> symbols, CancellationToken ct)
    {
        foreach (var symbol in symbols)
        {
            IAlpacaDataSubscription<IBar>? subscription;
            Action<IBar>? handler;
            lock (_symbolsLock)
            {
                _subscriptions.TryGetValue(symbol, out subscription);
                _barHandlers.TryGetValue(symbol, out handler);
            }

            if (subscription is null)
            {
                _logger.LogWarning("Subscription object not found for {Symbol}; skipping unsubscribe", symbol);
            }
            else
            {
                // BUG-C05 fix: detach the exact handler instance to stop stale bar callbacks,
                // then send the unsubscribe message to the server using the original subscription object.
                if (handler is not null)
                {
                    subscription.Received -= handler;
                }
                await client.UnsubscribeAsync(subscription, ct);
            }

            lock (_symbolsLock)
            {
                _subscribedSymbols.Remove(symbol);
                _subscriptions.Remove(symbol);
                _barHandlers.Remove(symbol);
            }
        }

        _logger.LogInformation("Unsubscribed from streaming for {Count} symbols: {Symbols}",
            symbols.Count, string.Join(", ", symbols));
    }

    private async Task<RealtimeMarketDataSelection> GetRuntimeSelectionAsync(
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var reader = scope.ServiceProvider
            .GetRequiredService<IRealtimeMarketDataSelectionReader>();
        return await reader.ReadAsync(ct);
    }

    private async Task<bool> IsAlpacaSelectedAsync(CancellationToken ct) =>
        (await GetRuntimeSelectionAsync(ct)).Source == DataSource.Alpaca;

    private TimeSpan CalculateBackoffDelay(int attempt)
    {
        var baseDelay = _streamingSettings.InitialReconnectDelaySeconds
            * Math.Pow(2, attempt - 1);
        var capped = Math.Min(baseDelay, _streamingSettings.MaxReconnectDelaySeconds);

        // Add 25% jitter
        var jitter = capped * 0.25 * Random.Shared.NextDouble();
        return TimeSpan.FromSeconds(capped + jitter);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _streamingStatus.MarkInactive();
        _notificationService.PublishStreamingStatus(false);
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("AlpacaStreamingService stopped");
    }
}
