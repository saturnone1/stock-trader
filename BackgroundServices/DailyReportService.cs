using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Services.Account;
using StockTrader.Services.Notification;
using TimeZoneConverter;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 매 거래일 설정된 시간에 일일 요약 리포트를 활성화된 모든 알림 채널로 발송하는 백그라운드 서비스.
/// 발송 시간 우선순위:
///   1. DB UserSettings.DailyReportTimeKst (KST 기준, 예: "07:30") → ET로 변환
///   2. appsettings.json Notification.DailyReportTime (ET 기준, 예: "16:30")
/// </summary>
public sealed class DailyReportService : BackgroundService
{
    private static readonly TimeZoneInfo EasternTime =
        TZConvert.GetTimeZoneInfo("America/New_York");
    private static readonly TimeZoneInfo KoreanTime =
        TZConvert.GetTimeZoneInfo("Asia/Seoul");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IAccountManager _accountManager;
    private readonly NotificationSettings _appNotificationSettings;
    private readonly ILogger<DailyReportService> _logger;

    public DailyReportService(
        IServiceScopeFactory scopeFactory,
        INotificationDispatcher dispatcher,
        IAccountManager accountManager,
        IOptions<NotificationSettings> notificationSettings,
        ILogger<DailyReportService> logger)
    {
        _scopeFactory             = scopeFactory;
        _dispatcher               = dispatcher;
        _accountManager           = accountManager;
        _appNotificationSettings  = notificationSettings.Value;
        _logger                   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DailyReportService started. Default report time: {Time} ET (DB override via DailyReportTimeKst supported)",
            _appNotificationSettings.DailyReportTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = await CalculateDelayToNextReportAsync(stoppingToken);
                _logger.LogDebug(
                    "Next daily report in {Hours:F1} hours", delay.TotalHours);

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested) break;

                await SendDailyReportAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DailyReportService — will retry in 1 hour");
                // 에러 발생 시 1시간 후 재시도 (무한 루프 방지)
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    /// <summary>
    /// DB UserSettings.DailyReportTimeKst (KST) 또는 appsettings DailyReportTime (ET)을
    /// 기준으로 다음 리포트 발송까지 남은 시간을 계산한다.
    /// </summary>
    private async Task<TimeSpan> CalculateDelayToNextReportAsync(CancellationToken ct)
    {
        // DB에서 KST 설정 조회 시도
        TimeSpan? reportTimeEt = null;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var userSettings = await settingsRepo.GetAsync(ct);

            if (!string.IsNullOrWhiteSpace(userSettings.DailyReportTimeKst)
                && TimeSpan.TryParse(userSettings.DailyReportTimeKst, out var kstTime))
            {
                // KST 시간을 ET로 변환: 오늘 날짜에 KST 시간을 적용한 후 타임존 변환
                var nowKst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KoreanTime);
                var kstDateTime = nowKst.Date + kstTime;

                // KST → UTC → ET
                var kstDateTimeUtc = TimeZoneInfo.ConvertTimeToUtc(kstDateTime, KoreanTime);
                var etDateTime = TimeZoneInfo.ConvertTimeFromUtc(kstDateTimeUtc, EasternTime);

                reportTimeEt = etDateTime.TimeOfDay;
                _logger.LogDebug(
                    "Using DB DailyReportTimeKst={Kst} → ET={Et}",
                    userSettings.DailyReportTimeKst, etDateTime.ToString("HH:mm"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DB DailyReportTimeKst 조회 실패 — appsettings fallback 사용");
        }

        // DB 설정이 없으면 appsettings ET 시간 사용
        if (reportTimeEt == null)
        {
            if (!TimeSpan.TryParse(_appNotificationSettings.DailyReportTime, out var parsedEt))
                parsedEt = TimeSpan.FromHours(16.5); // 최종 기본값 16:30 ET
            reportTimeEt = parsedEt;
        }

        return CalculateDelayFromEtTime(reportTimeEt.Value);
    }

    /// <summary>ET 기준 시간(TimeSpan)으로 다음 영업일 발송까지 남은 Delay를 계산한다.</summary>
    private static TimeSpan CalculateDelayFromEtTime(TimeSpan reportTimeEt)
    {
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTime);
        var todayReport = nowEt.Date + reportTimeEt;

        // 오늘 리포트 시간이 이미 지났으면 다음 영업일 리포트 시간으로 계산
        var nextReport = nowEt < todayReport ? todayReport : todayReport.AddDays(1);

        // 주말 스킵: 토요일 → 월요일, 일요일 → 월요일
        while (nextReport.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            nextReport = nextReport.AddDays(1);

        var nextReportUtc = TimeZoneInfo.ConvertTimeToUtc(nextReport, EasternTime);
        var delay = nextReportUtc - DateTime.UtcNow;

        // 음수 방지 (타이밍 경계 조건)
        return delay < TimeSpan.Zero ? TimeSpan.FromMinutes(1) : delay;
    }

    private async Task SendDailyReportAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();

        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTime);
        var today = DateOnly.FromDateTime(nowEt);
        var todayStart = TimeZoneInfo.ConvertTimeToUtc(nowEt.Date, EasternTime);
        var todayEnd = todayStart.AddDays(1);

        try
        {
            // 오늘 체결된 거래 조회
            var todayTrades = await tradeRepo.GetTradesAsync(
                patternType: null,
                from: todayStart,
                to: todayEnd,
                ct: ct);

            // 오늘 발생한 시그널/추천 조회 (최대 50개)
            var recentRecs = await tradeRepo.GetRecentRecommendationsAsync(count: 50, ct: ct);
            var todayRecs = recentRecs
                .Where(r => r.GeneratedAt >= todayStart && r.GeneratedAt < todayEnd)
                .ToList();

            // 손익 계산 (equity 기반)
            // 포지션 사이즈가 다를 때 진입금액 합계로 나누면 왜곡됨 →
            // 활성 계좌의 총 자산(equity)을 분모로 사용한다.
            var dailyPnl = todayTrades.Sum(t => t.PnL);
            decimal dailyPnlPercent = 0m;
            try
            {
                var broker = await _accountManager.GetActiveBrokerServiceAsync(ct);
                if (broker != null)
                {
                    var brokerAccount = await broker.GetAccountAsync(ct);
                    var totalEquity = brokerAccount?.TotalEquity ?? 0m;
                    if (totalEquity > 0m)
                    {
                        dailyPnlPercent = dailyPnl / totalEquity * 100m;
                    }
                    else if (dailyPnl != 0m)
                    {
                        // equity 조회 실패 → 진입금액 합계로 대체 (부정확하지만 0보다 나음)
                        var initialValue = todayTrades.Sum(t => t.EntryPrice * t.Quantity);
                        dailyPnlPercent = initialValue > 0m ? dailyPnl / initialValue * 100m : 0m;
                        _logger.LogDebug(
                            "Daily PnL%: broker equity unavailable, falling back to entry-value denominator");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not fetch broker equity for daily PnL% calculation");
            }

            // 주요 시그널 요약 (상위 5건)
            var topSignals = todayRecs
                .Take(5)
                .Select(r => $"{r.Symbol} ({r.PatternType}) @ ${r.EntryPrice:F2}")
                .ToList();

            // 체결 종목
            var executedSymbols = todayTrades
                .Select(t => t.Symbol)
                .Distinct()
                .ToList();

            var report = new DailyReportData(
                ReportDate: today,
                TotalSignals: todayRecs.Count,
                ExecutedTrades: todayTrades.Count,
                DailyPnl: dailyPnl,
                DailyPnlPercent: dailyPnlPercent,
                TopSignals: topSignals,
                ExecutedSymbols: executedSymbols,
                MarketRegimeSummary: "N/A" // 레짐 정보는 런타임 캐시에만 존재
            );

            await _dispatcher.DispatchDailyReportAsync(report, ct);

            _logger.LogInformation(
                "Daily report sent: {Signals} signals, {Trades} trades, PnL ${Pnl:N2}",
                todayRecs.Count, todayTrades.Count, dailyPnl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send daily report for {Date}", today);
            throw; // 상위에서 재시도 처리
        }
    }
}
