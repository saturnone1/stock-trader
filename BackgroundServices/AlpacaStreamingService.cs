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

    private IAlpacaDataStreamingClient? _client;
    private HashSet<string> _subscribedSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, decimal> _previousClose = new(StringComparer.OrdinalIgnoreCase);

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
                    return;
                }

                var delay = CalculateBackoffDelay(attempt);
                _logger.LogWarning(ex,
                    "Streaming connection lost (attempt {Attempt}/{Max}). Reconnecting in {Delay}s",
                    attempt, _settings.MaxReconnectAttempts, delay.TotalSeconds);

                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    private async Task ConnectAndStreamAsync(CancellationToken ct)
    {
        var secretKey = new SecretKey(_settings.ApiKey, _settings.ApiSecret);

        _client = _settings.IsPaper
            ? Alpaca.Markets.Environments.Paper.GetAlpacaDataStreamingClient(secretKey)
            : Alpaca.Markets.Environments.Live.GetAlpacaDataStreamingClient(secretKey);

        using var client = _client;

        var authStatus = await client.ConnectAndAuthenticateAsync(ct);
        if (authStatus != AuthStatus.Authorized)
        {
            throw new InvalidOperationException($"Alpaca streaming auth failed: {authStatus}");
        }

        _logger.LogInformation("Alpaca WebSocket streaming connected and authenticated");

        // Initial subscription
        var symbols = await GetWatchlistSymbolsAsync(ct);
        if (symbols.Count > 0)
        {
            await SubscribeToSymbolsAsync(client, symbols, ct);
        }

        _notificationService.PublishStreamingStatus(true);

        // Run watchlist sync and keep-alive in parallel
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await WatchlistSyncLoopAsync(client, cts.Token);
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

                var toSubscribe = currentSet.Except(_subscribedSymbols, StringComparer.OrdinalIgnoreCase).ToList();
                var toUnsubscribe = _subscribedSymbols.Except(currentSet, StringComparer.OrdinalIgnoreCase).ToList();

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
            subscription.Received += bar => _ = ProcessBarAsync(symbol, bar);
            await client.SubscribeAsync(subscription, ct);
            _subscribedSymbols.Add(symbol);
        }

        _logger.LogInformation("Subscribed to streaming for {Count} symbols: {Symbols}",
            symbols.Count, string.Join(", ", symbols));
    }

    private async Task UnsubscribeFromSymbolsAsync(
        IAlpacaDataStreamingClient client, IReadOnlyList<string> symbols, CancellationToken ct)
    {
        foreach (var symbol in symbols)
        {
            var subscription = client.GetMinuteBarSubscription(symbol);
            await client.UnsubscribeAsync(subscription, ct);
            _subscribedSymbols.Remove(symbol);
        }

        _logger.LogInformation("Unsubscribed from streaming for {Count} symbols: {Symbols}",
            symbols.Count, string.Join(", ", symbols));
    }

    private async Task ProcessBarAsync(string symbol, IBar bar)
    {
        try
        {
            _streamingStatus.MarkActive(DateTime.UtcNow);

            // Save bar to DB
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

            using var scope = _scopeFactory.CreateScope();
            var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
            await ohlcvRepo.AddBarsAsync([ohlcvBar]);

            // Push symbol to pattern scanner channel
            await _symbolChannel.Writer.WriteAsync(symbol);

            // Publish price update for UI
            var previousClose = _previousClose.GetValueOrDefault(symbol, bar.Open);
            var change = bar.Close - previousClose;
            var changePercent = previousClose != 0 ? change / previousClose * 100 : 0;

            _notificationService.PublishPriceUpdate(new PriceUpdate(
                symbol, bar.Close, change, changePercent, (long)bar.Volume, bar.TimeUtc));

            _notificationService.PublishBarUpdate(symbol);

            _previousClose[symbol] = bar.Close;

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
