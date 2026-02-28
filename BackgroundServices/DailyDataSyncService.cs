using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Statistics;
using TimeZoneConverter;

namespace StockTrader.BackgroundServices;

public class DailyDataSyncService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TradingSettings _settings;
    private readonly ILogger<DailyDataSyncService> _logger;

    private int _consecutiveFailures = 0;

    public DailyDataSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<TradingSettings> settings,
        ILogger<DailyDataSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailyDataSyncService started");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TZConvert.GetTimeZoneInfo("America/New_York"));

            if (now.DayOfWeek == DayOfWeek.Saturday || now.DayOfWeek == DayOfWeek.Sunday)
                continue;

            var closeTime = TimeSpan.Parse(_settings.MarketCloseET);
            if (now.TimeOfDay < closeTime.Add(TimeSpan.FromHours(1)))
                continue;

            // Circuit breaker: cooldown when too many consecutive failures.
            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                _logger.LogWarning(
                    "{Service} entering cooldown after {Failures} consecutive failures. " +
                    "Waiting {Cooldown} before resuming",
                    nameof(DailyDataSyncService), _consecutiveFailures, CooldownPeriod);

                await Task.Delay(CooldownPeriod, stoppingToken);
                _consecutiveFailures = 0;
            }

            try
            {
                await RetryHelper.ExecuteWithRetryAsync(
                    () => SyncDailyDataAsync(stoppingToken),
                    _logger,
                    "DailyDataSync",
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
                    "Error during daily data sync (consecutive failures: {Failures})",
                    _consecutiveFailures);
            }
        }
    }

    private async Task SyncDailyDataAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataFeedFactory = scope.ServiceProvider.GetRequiredService<IDataFeedServiceFactory>();
        var dataFeed = await dataFeedFactory.GetServiceAsync(ct);
        var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var statsService = scope.ServiceProvider.GetRequiredService<IStatisticsService>();

        var settings = await settingsRepo.GetAsync(ct);

        foreach (var symbol in settings.WatchlistSymbols)
        {
            try
            {
                var lastDate = await ohlcvRepo.GetLastTimestampAsync(symbol, TimeFrame.Daily, ct);
                var from = lastDate?.AddDays(1) ?? DateTime.UtcNow.AddYears(-5);

                var bars = await dataFeed.GetHistoricalBarsAsync(
                    symbol, TimeFrame.Daily, from, DateTime.UtcNow, ct);

                if (bars.Count > 0)
                {
                    await ohlcvRepo.AddBarsAsync(bars, ct);
                    _logger.LogInformation("Synced {Count} daily bars for {Symbol}",
                        bars.Count, symbol);
                }
            }
            catch (Exception ex)
            {
                // Per-symbol errors are logged but do not abort the whole sync cycle.
                _logger.LogError(ex, "Error syncing daily data for {Symbol}", symbol);
            }
        }

        await statsService.RefreshAllStatsAsync(ct);
    }
}
