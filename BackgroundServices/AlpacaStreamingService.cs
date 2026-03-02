using System.Threading.Channels;
using Alpaca.Markets;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Notification;
using StockTrader.Services.Streaming;
using TimeFrame = StockTrader.Models.Enums.TimeFrame;

namespace StockTrader.BackgroundServices;

public class AlpacaStreamingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _symbolChannel;
    private readonly AlpacaSettings _settings;
    private readonly IStreamingStatusService _streamingStatus;
    private readonly INotificationService _notificationService;
    private readonly ILogger<AlpacaStreamingService> _logger;

    // Bar batching buffer: bars are written here and flushed to DB every 5 seconds
    private readonly Channel<OhlcvBar> _barBuffer = Channel.CreateUnbounded<OhlcvBar>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    private readonly HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    // BUG-C05 fix: store subscription objects keyed by symbol so unsubscribe reuses the same instance
    private readonly Dictionary<string, IAlpacaDataSubscription<IBar>> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    // BUG-C05 fix: store named handlers so -= removes the exact same delegate reference
    private readonly Dictionary<string, Action<IBar>> _barHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _symbolsLock = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _previousClose = new(StringComparer.OrdinalIgnoreCase);

    public AlpacaStreamingService(
        IServiceScopeFactory scopeFactory,
        Channel<string> symbolChannel,
        IOptions<AlpacaSettings> settings,
        IStreamingStatusService streamingStatus,
        INotificationService notificationService,
        ILogger<AlpacaStreamingService> logger)
    {
        _scopeFactory = scopeFactory;
        _symbolChannel = symbolChannel;
        _settings = settings.Value;
        _streamingStatus = streamingStatus;
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableStreaming)
        {
            _logger.LogInformation("AlpacaStreamingService disabled (EnableStreaming=false)");
            return;
        }

        _logger.LogInformation("AlpacaStreamingService starting");

        // Start the bar-flush loop as a concurrent background task
        var flushTask = BarFlushLoopAsync(stoppingToken);

        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndStreamAsync(stoppingToken);
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

                if (attempt > _settings.MaxReconnectAttempts)
                {
                    _logger.LogWarning(
                        "Max reconnect attempts ({Max}) exceeded. Falling back to polling",
                        _settings.MaxReconnectAttempts);
                    break;
                }

                var delay = CalculateBackoffDelay(attempt);
                _logger.LogWarning(ex,
                    "Streaming connection lost (attempt {Attempt}/{Max}). Reconnecting in {Delay}s",
                    attempt, _settings.MaxReconnectAttempts, delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
            }
        }

        // Signal the flush loop to drain remaining bars, then wait for it to finish
        _barBuffer.Writer.TryComplete();
        await flushTask;
    }

    /// <summary>
    /// Drains <see cref="_barBuffer"/> on a 5-second periodic timer and persists all
    /// buffered bars in a single DI scope / DB transaction, reducing per-bar scope overhead.
    /// </summary>
    private async Task BarFlushLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        // Keep flushing until the timer is cancelled AND the channel is drained
        while (true)
        {
            // Wait for the next tick; if cancelled, do a final drain pass then exit
            var ticked = false;
            try
            {
                ticked = await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested — perform one final flush then return
                await FlushBarBatchAsync(CancellationToken.None);
                return;
            }

            if (!ticked) break;

            await FlushBarBatchAsync(ct);
        }
    }

    private async Task FlushBarBatchAsync(CancellationToken ct)
    {
        var batch = new List<OhlcvBar>();

        // Drain all currently available bars without blocking
        while (_barBuffer.Reader.TryRead(out var bar))
            batch.Add(bar);

        if (batch.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
            await ohlcvRepo.AddBarsAsync(batch, ct);

            _logger.LogDebug("Bar flush: persisted {Count} bars to DB", batch.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing {Count} streaming bars to DB", batch.Count);
        }
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

        // Clear stale subscription tracking from any previous connection attempt
        lock (_symbolsLock)
        {
            _subscribedSymbols.Clear();
            _subscriptions.Clear();
            _barHandlers.Clear();
        }

        // Initial subscription
        var symbols = await GetWatchlistSymbolsAsync(ct);
        if (symbols.Count > 0)
        {
            await SubscribeToSymbolsAsync(client, symbols, cts.Token);
        }

        _notificationService.PublishStreamingStatus(true);

        // Run watchlist sync loop — exits when ct (app stop) or cts (socket closed) is cancelled
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
    }

    private async Task WatchlistSyncLoopAsync(IAlpacaDataStreamingClient client, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var currentSymbols = await GetWatchlistSymbolsAsync(ct);
                var currentSet = new HashSet<string>(currentSymbols, StringComparer.OrdinalIgnoreCase);

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

            // BUG-C05 fix: store the handler instance so unsubscribe can remove the exact delegate
            var capturedSymbol = symbol;
            Action<IBar> handler = bar => _ = ProcessBarAsync(capturedSymbol, bar);
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

    private async Task ProcessBarAsync(string symbol, IBar bar)
    {
        try
        {
            _streamingStatus.MarkActive(DateTime.UtcNow);

            // Buffer bar for batch DB persist — BarFlushLoopAsync drains every 5 seconds
            var ohlcvBar = new OhlcvBar
            {
                Symbol = symbol,
                Timestamp = bar.TimeUtc,
                TimeFrame = TimeFrame.OneMinute,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = (long)bar.Volume,
                Vwap = bar.Vwap
            };
            await _barBuffer.Writer.WriteAsync(ohlcvBar);

            // Push symbol to pattern scanner channel
            await _symbolChannel.Writer.WriteAsync(symbol);

            // Publish price update for UI
            var previousClose = _previousClose.GetValueOrDefault(symbol, bar.Open);
            var change = bar.Close - previousClose;
            var changePercent = previousClose != 0 ? change / previousClose * 100 : 0;

            _notificationService.PublishPriceUpdate(new PriceUpdate(
                symbol, bar.Close, change, changePercent, (long)bar.Volume, bar.TimeUtc));

            _notificationService.PublishBarUpdate(symbol);

            _previousClose[symbol] = bar.Close; // ConcurrentDictionary: thread-safe indexer

            _logger.LogDebug("Streaming bar received: {Symbol} C={Close} V={Volume}",
                symbol, bar.Close, bar.Volume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing streaming bar for {Symbol}", symbol);
        }
    }

    private async Task<List<string>> GetWatchlistSymbolsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var settings = await settingsRepo.GetAsync(ct);
        return settings.WatchlistSymbols;
    }

    private TimeSpan CalculateBackoffDelay(int attempt)
    {
        var baseDelay = _settings.InitialReconnectDelaySeconds * Math.Pow(2, attempt - 1);
        var capped = Math.Min(baseDelay, _settings.MaxReconnectDelaySeconds);

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
