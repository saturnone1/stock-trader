using System.Threading.Channels;
using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Streaming;

namespace StockTrader.BackgroundServices;

public class MarketDataIngestionService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<string> _symbolChannel;
    private readonly TradingSettings _settings;
    private readonly IStreamingStatusService _streamingStatus;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ILogger<MarketDataIngestionService> _logger;

    private int _consecutiveFailures = 0;

    public MarketDataIngestionService(
        IServiceScopeFactory scopeFactory,
        Channel<string> symbolChannel,
        IOptions<TradingSettings> settings,
        IStreamingStatusService streamingStatus,
        IMarketCalendar marketCalendar,
        ILogger<MarketDataIngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _symbolChannel = symbolChannel;
        _settings = settings.Value;
        _streamingStatus = streamingStatus;
        _marketCalendar = marketCalendar;
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
        var ingested = 0;
        var errors = 0;

        // Collect all bars first; write to channel per-symbol so scanners get notified promptly.
        var batchBars = new List<Models.OhlcvBar>(settings.WatchlistSymbols.Count);
        var successfulSymbols = new List<string>(settings.WatchlistSymbols.Count);

        foreach (var symbol in settings.WatchlistSymbols)
        {
            try
            {
                var bar = await dataFeed.GetLatestBarAsync(symbol, TimeFrame.OneMinute, ct);
                if (bar != null)
                {
                    batchBars.Add(bar);
                    successfulSymbols.Add(symbol);
                    ingested++;
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Error ingesting data for {Symbol}", symbol);
            }
        }

        // Single batch INSERT — one transaction instead of N individual commits.
        if (batchBars.Count > 0)
        {
            await ohlcvRepo.AddBarsAsync(batchBars, ct);
        }

        // Notify pattern scanner per-symbol (order preserved).
        foreach (var symbol in successfulSymbols)
        {
            await _symbolChannel.Writer.WriteAsync(symbol, ct);
        }

        _logger.LogInformation(
            "Ingestion cycle complete: {Ingested}/{Total} symbols ingested, {Errors} errors",
            ingested, settings.WatchlistSymbols.Count, errors);
    }

    private bool IsMarketHours()
    {
        // US 또는 KRX 어느 쪽이든 장이 열려 있으면 true
        return _marketCalendar.IsMarketOpen(MarketRegion.UnitedStates)
            || _marketCalendar.IsMarketOpen(MarketRegion.Korea);
    }
}
