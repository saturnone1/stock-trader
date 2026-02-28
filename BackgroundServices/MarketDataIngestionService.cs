using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Streaming;
using TimeZoneConverter;

namespace StockTrader.BackgroundServices;

public class MarketDataIngestionService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _symbolChannel;
    private readonly TradingSettings _settings;
    private readonly IStreamingStatusService _streamingStatus;
    private readonly ILogger<MarketDataIngestionService> _logger;

    private int _consecutiveFailures = 0;

    public MarketDataIngestionService(
        IServiceScopeFactory scopeFactory,
        Channel<string> symbolChannel,
        IOptions<TradingSettings> settings,
        IStreamingStatusService streamingStatus,
        ILogger<MarketDataIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _symbolChannel = symbolChannel;
        _settings = settings.Value;
        _streamingStatus = streamingStatus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MarketDataIngestionService started");

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_settings.DataFetchIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!IsMarketHours())
            {
                continue;
            }

            if (_streamingStatus.IsStreamingActive)
            {
                _logger.LogDebug("Streaming active — skipping REST polling cycle");
                continue;
            }

            // Circuit breaker: cooldown when too many consecutive failures.
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                _logger.LogWarning(
                    "{Service} entering cooldown after {Failures} consecutive failures. " +
                    "Waiting {Cooldown} before resuming",
                    nameof(MarketDataIngestionService), _consecutiveFailures, CooldownPeriod);

                await Task.Delay(CooldownPeriod, stoppingToken);
                _consecutiveFailures = 0;
            }

            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => IngestDataAsync(stoppingToken),
                    _logger,
                    "MarketDataIngestion",
                    maxRetries: 3,
                    ct: stoppingToken);

                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex,
                    "Error during market data ingestion (consecutive failures: {Failures})",
                    _consecutiveFailures);
            }
        }
    }

    private async Task IngestDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataFeedFactory = scope.ServiceProvider.GetRequiredService<IDataFeedServiceFactory>();
        var dataFeed = await dataFeedFactory.GetServiceAsync(ct);
        var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        var settings = await settingsRepo.GetAsync(ct);

        foreach (var symbol in settings.WatchlistSymbols)
        {
            try
            {
                var bar = await dataFeed.GetLatestBarAsync(symbol, TimeFrame.OneMinute, ct);
                if (bar != null)
                {
                    await ohlcvRepo.AddBarsAsync(new[] { bar }, ct);
                    await _symbolChannel.Writer.WriteAsync(symbol, ct);
                }
            }
            catch (Exception ex)
            {
                // Per-symbol errors are logged but do not abort the whole ingestion cycle.
                _logger.LogError(ex, "Error ingesting data for {Symbol}", symbol);
            }
        }
    }

    private bool IsMarketHours()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TZConvert.GetTimeZoneInfo("America/New_York"));

        if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
            return false;

        var open = TimeSpan.Parse(_settings.MarketOpenET);
        var close = TimeSpan.Parse(_settings.MarketCloseET);
        return now.TimeOfDay >= open && now.TimeOfDay <= close;
    }
}
