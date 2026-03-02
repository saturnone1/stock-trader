using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Market;
using StockTrader.Services.Statistics;

namespace StockTrader.BackgroundServices;

public class DailyDataSyncService : BackgroundService
{
    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CooldownPeriod = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TradingSettings _settings;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ILogger<DailyDataSyncService> _logger;

    private int _consecutiveFailures = 0;
    private DateTime _lastSyncDateUtc = DateTime.MinValue;

    public DailyDataSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<TradingSettings> settings,
        IMarketCalendar marketCalendar,
        ILogger<DailyDataSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _marketCalendar = marketCalendar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailyDataSyncService started");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // 오늘 이미 싱크 완료했으면 스킵 (30분마다 tick이 오지만 하루 1회로 제한)
            if (DateTime.UtcNow.Date == _lastSyncDateUtc)
                continue;

            // US 또는 KRX 장 마감 후 1시간이 지나야 동기화 시작
            var usNow = _marketCalendar.GetLocalNow(MarketType.US);
            var krxNow = _marketCalendar.GetLocalNow(MarketType.KRX);

            var usWeekend = usNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var krxWeekend = krxNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var usClose = _marketCalendar.GetMarketClose(MarketType.US);
            var krxClose = _marketCalendar.GetMarketClose(MarketType.KRX);
            var usReady = !usWeekend && usNow.TimeOfDay >= usClose.Add(TimeSpan.FromHours(1));
            var krxReady = !krxWeekend && krxNow.TimeOfDay >= krxClose.Add(TimeSpan.FromHours(1));

            if (!usReady && !krxReady)
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

                _lastSyncDateUtc = DateTime.UtcNow.Date;
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
        var synced = 0;
        var totalBars = 0;
        var errors = 0;

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
                    synced++;
                    totalBars += bars.Count;
                    _logger.LogDebug("Synced {Count} daily bars for {Symbol}", bars.Count, symbol);
                }
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex, "Error syncing daily data for {Symbol}", symbol);
            }
        }

        try
        {
            await statsService.RefreshAllStatsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "통계 갱신 중 오류 (데이터 동기화는 완료됨)");
        }

        _logger.LogInformation(
            "Daily sync complete: {Synced}/{Total} symbols, {Bars} bars synced, {Errors} errors",
            synced, settings.WatchlistSymbols.Count, totalBars, errors);
    }
}
