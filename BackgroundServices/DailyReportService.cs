using Microsoft.Extensions.Options;
using StockTrader.Application.MarketData;
using StockTrader.Application.Reporting;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;

namespace StockTrader.BackgroundServices;

/// <summary>계산과 데이터 접근을 애플리케이션 유스케이스에 위임하는 일일 리포트 스케줄 어댑터입니다.</summary>
public sealed class DailyReportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMarketCalendar _marketCalendar;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationSettings _settings;
    private readonly ILogger<DailyReportService> _logger;

    public DailyReportService(
        IServiceScopeFactory scopeFactory,
        IMarketCalendar marketCalendar,
        TimeProvider timeProvider,
        IOptions<NotificationSettings> settings,
        ILogger<DailyReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _marketCalendar = marketCalendar;
        _timeProvider = timeProvider;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DailyReportService started. Default report time: {Time} ET",
            _settings.DailyReportTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = await GetDelayAsync(stoppingToken);
                _logger.LogDebug("Next daily report in {Hours:F1} hours", delay.TotalHours);
                await Task.Delay(delay, _timeProvider, stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                    await GenerateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                var retryDelay = TimeSpan.FromMinutes(
                    _settings.DailyReportRetryDelayMinutes);
                _logger.LogError(
                    ex,
                    "Error in DailyReportService — retrying in {Minutes} minutes",
                    retryDelay.TotalMinutes);
                await Task.Delay(retryDelay, _timeProvider, stoppingToken);
            }
        }
    }

    private async Task<TimeSpan> GetDelayAsync(CancellationToken ct)
    {
        TimeOnly? koreanReportTime = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var schedule = scope.ServiceProvider
                .GetRequiredService<IDailyReportScheduleQuery>();
            koreanReportTime = await schedule.GetKoreanReportTimeAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Daily report schedule lookup failed — using configured ET fallback");
        }

        var marketTimeZone = _marketCalendar.GetTimeZone(MarketRegion.UnitedStates);
        var reportTime = koreanReportTime
            ?? TimeOnly.ParseExact(_settings.DailyReportTime, "HH:mm");
        var reportTimeZone = koreanReportTime.HasValue
            ? _marketCalendar.GetTimeZone(MarketRegion.Korea)
            : marketTimeZone;

        return DailyReportPolicy.CalculateDelay(
            _timeProvider.GetUtcNow(),
            reportTime,
            reportTimeZone,
            marketTimeZone,
            _marketCalendar.TradingDayPredicate(MarketRegion.UnitedStates));
    }

    private async Task GenerateAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IDailyReportGenerator>();
        var report = await generator.GenerateAndPublishAsync(
            _marketCalendar.GetTimeZone(MarketRegion.UnitedStates),
            ct);
        _logger.LogInformation(
            "Daily report sent: {Signals} signals, {Trades} trades, PnL ${Pnl:N2}",
            report.TotalSignals,
            report.ExecutedTrades,
            report.DailyPnl);
    }
}
