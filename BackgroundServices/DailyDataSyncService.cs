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

    // BUG-W04: 동일 날짜 중복 싱크 방지 — 마지막 싱크 완료 날짜(UTC)를 기록한다.
    // PeriodicTimer는 30분마다 tick을 발생시키지만, 싱크는 하루에 단 1회만 실행해야 한다.
    // DateOnly 사용: DateTime.Date 비교는 tick 경계에서 race condition 발생 가능.
    private DateOnly _lastSyncDate = DateOnly.MinValue;

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

        // 시작 시 daily bars가 부족하면 즉시 동기화 (패턴 스캐너가 데이터 없이 스킵하는 문제 방지)
        await RunInitialSyncIfNeededAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // BUG-W04: 오늘 이미 싱크 완료했으면 스킵.
            // 장 마감 조건(usReady/krxReady)이 30분 tick마다 계속 true가 되어
            // 동일 날짜 데이터를 하루에 수십 번 재싱크하는 문제를 방지한다.
            if (DateOnly.FromDateTime(DateTime.UtcNow) == _lastSyncDate)
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
                var errors = 0;
                await RetryHelper.ExecuteWithRetryAsync(
                    async () => { errors = await SyncDailyDataAsync(stoppingToken); },
                    _logger,
                    "DailyDataSync",
                    maxRetries: 3,
                    ct: stoppingToken);

                // BUG-W04: 모든 심볼이 성공했을 때만 오늘 날짜를 기록한다.
                // 부분 실패(일부 심볼 오류) 시에는 날짜를 기록하지 않아 다음 tick에서 재시도한다.
                if (errors == 0)
                    _lastSyncDate = DateOnly.FromDateTime(DateTime.UtcNow);
                else
                    _logger.LogWarning("Partial sync: {Errors} symbols failed, will retry next cycle", errors);

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

    /// <summary>
    /// 앱 시작 시 daily bars가 부족한 심볼이 있으면 즉시 동기화.
    /// PatternScanner가 bars.Count &lt; 20으로 스킵하는 문제를 방지한다.
    /// DailyDataSync는 장 마감 후에만 동작하므로, 장중 시작 시 데이터가 없을 수 있다.
    /// </summary>
    private async Task RunInitialSyncIfNeededAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

            var settings = await settingsRepo.GetAsync(ct);
            var symbolsNeedingSync = new List<string>();

            // SPY는 레짐 계산(SMA200)에 최소 200개 daily bars 필요 → 별도 임계값 적용
            foreach (var symbol in settings.WatchlistSymbols)
            {
                var minBars = symbol.Equals("SPY", StringComparison.OrdinalIgnoreCase) ? 200 : 20;
                var bars = await ohlcvRepo.GetBarsAsync(symbol, TimeFrame.Daily,
                    DateTime.UtcNow.AddDays(-400), DateTime.UtcNow, ct);
                if (bars.Count < minBars)
                    symbolsNeedingSync.Add(symbol);
            }

            // SPY가 워치리스트에 없어도 레짐 계산에 필요하므로 확인
            if (!settings.WatchlistSymbols.Any(s => s.Equals("SPY", StringComparison.OrdinalIgnoreCase)))
            {
                var spyBars = await ohlcvRepo.GetBarsAsync("SPY", TimeFrame.Daily,
                    DateTime.UtcNow.AddDays(-400), DateTime.UtcNow, ct);
                if (spyBars.Count < 200)
                    symbolsNeedingSync.Add("SPY");
            }

            if (symbolsNeedingSync.Count == 0)
            {
                _logger.LogInformation("Initial sync: all {Count} symbols have sufficient daily bars",
                    settings.WatchlistSymbols.Count);
                return;
            }

            _logger.LogInformation(
                "Initial sync: {NeedSync}/{Total} symbols need daily bars — syncing now: {Symbols}",
                symbolsNeedingSync.Count, settings.WatchlistSymbols.Count,
                string.Join(", ", symbolsNeedingSync));

            var dataFeedFactory = scope.ServiceProvider.GetRequiredService<IDataFeedServiceFactory>();
            var dataFeed = await dataFeedFactory.GetServiceAsync(ct);
            var synced = 0;

            foreach (var symbol in symbolsNeedingSync)
            {
                try
                {
                    var bars = await dataFeed.GetHistoricalBarsAsync(
                        symbol, TimeFrame.Daily,
                        DateTime.UtcNow.AddDays(-400), DateTime.UtcNow, ct);

                    if (bars.Count > 0)
                    {
                        await ohlcvRepo.AddBarsAsync(bars, ct);
                        synced++;
                        _logger.LogDebug("Initial sync: {Count} daily bars for {Symbol}", bars.Count, symbol);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Initial sync failed for {Symbol} — will retry at regular sync", symbol);
                }
            }

            _logger.LogInformation("Initial sync complete: {Synced}/{NeedSync} symbols synced",
                synced, symbolsNeedingSync.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial sync check failed — scanner will use available data");
        }
    }

    /// <summary>
    /// 모든 심볼의 일봉 데이터를 동기화한다.
    /// Returns: 동기화 중 발생한 심볼별 오류 수 (0 = 완전 성공).
    /// </summary>
    private async Task<int> SyncDailyDataAsync(CancellationToken ct)
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

        return errors;
    }
}
