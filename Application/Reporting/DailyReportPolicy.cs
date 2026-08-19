namespace StockTrader.Application.Reporting;

public sealed record DailyReportWindow(
    DateOnly ReportDate,
    DateTime FromUtc,
    DateTime ToUtc);

public static class DailyReportPolicy
{
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMinutes(1);

    public static DailyReportWindow ResolveMarketDay(
        DateTimeOffset observation,
        TimeZoneInfo marketTimeZone)
    {
        var marketNow = TimeZoneInfo.ConvertTime(observation, marketTimeZone);
        var localStart = DateTime.SpecifyKind(marketNow.Date, DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);

        return new DailyReportWindow(
            DateOnly.FromDateTime(localStart),
            TimeZoneInfo.ConvertTimeToUtc(localStart, marketTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, marketTimeZone));
    }

    /// <summary>
    /// 다음 보고 시각까지의 지연. 거래가 없었던 날에는 보고하지 않으므로
    /// 실제 거래일이 될 때까지 후보 날짜를 밀어낸다.
    /// 거래일 여부는 <paramref name="isMarketTradingDay"/> 가 판정하며, 호출자가
    /// 거래소 캘린더를 연결한다. 연속 휴장이 아무리 길어도 탐색은 유한하게 끝난다.
    /// </summary>
    public static TimeSpan CalculateDelay(
        DateTimeOffset observation,
        TimeOnly reportTime,
        TimeZoneInfo reportTimeZone,
        TimeZoneInfo marketTimeZone,
        Func<DateOnly, bool> isMarketTradingDay)
    {
        var reportLocalNow = TimeZoneInfo.ConvertTime(observation, reportTimeZone);
        var candidate = DateTime.SpecifyKind(
            reportLocalNow.Date + reportTime.ToTimeSpan(),
            DateTimeKind.Unspecified);
        if (candidate <= reportLocalNow.DateTime)
            candidate = candidate.AddDays(1);

        // 어떤 시장도 연속 휴장이 이보다 길지 않다. 판정이 계속 거짓을 반환하더라도
        // 무한 루프에 빠지지 않고 마지막 후보로 예약한 뒤 다음 주기에 재평가한다.
        const int maximumConsecutiveNonTradingDays = 14;

        for (var attempt = 0; attempt <= maximumConsecutiveNonTradingDays; attempt++)
        {
            var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, reportTimeZone);
            var marketDate = TimeZoneInfo.ConvertTimeFromUtc(candidateUtc, marketTimeZone);
            if (attempt == maximumConsecutiveNonTradingDays
                || isMarketTradingDay(DateOnly.FromDateTime(marketDate)))
            {
                var delay = candidateUtc - observation.UtcDateTime;
                return delay < TimeSpan.Zero ? MinimumDelay : delay;
            }

            candidate = candidate.AddDays(1);
        }

        return MinimumDelay;
    }

    public static DailyReportData Create(
        DateOnly reportDate,
        DailyReportActivitySnapshot activity,
        decimal? accountEquity)
    {
        var dailyPnl = activity.Trades.Sum(trade => trade.PnL);
        var entryValue = activity.Trades.Sum(trade => trade.EntryPrice * trade.Quantity);
        var denominator = accountEquity is > 0m ? accountEquity.Value : entryValue;
        var dailyPnlPercent = denominator > 0m
            ? dailyPnl / denominator * 100m
            : 0m;

        var topSignals = activity.Signals
            .OrderByDescending(signal => signal.GeneratedAt)
            .ThenBy(signal => signal.Symbol, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(signal =>
                $"{signal.Symbol} ({signal.PatternType}) @ ${signal.EntryPrice:F2}")
            .ToArray();
        var executedSymbols = activity.Trades
            .OrderBy(trade => trade.ExitTime)
            .ThenBy(trade => trade.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(trade => trade.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DailyReportData(
            reportDate,
            activity.Signals.Count,
            activity.Trades.Count,
            dailyPnl,
            dailyPnlPercent,
            topSignals,
            executedSymbols,
            "N/A");
    }
}
