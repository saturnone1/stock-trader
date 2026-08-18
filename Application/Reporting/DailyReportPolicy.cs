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

    public static TimeSpan CalculateDelay(
        DateTimeOffset observation,
        TimeOnly reportTime,
        TimeZoneInfo reportTimeZone,
        TimeZoneInfo marketTimeZone)
    {
        var reportLocalNow = TimeZoneInfo.ConvertTime(observation, reportTimeZone);
        var candidate = DateTime.SpecifyKind(
            reportLocalNow.Date + reportTime.ToTimeSpan(),
            DateTimeKind.Unspecified);
        if (candidate <= reportLocalNow.DateTime)
            candidate = candidate.AddDays(1);

        while (true)
        {
            var candidateUtc = TimeZoneInfo.ConvertTimeToUtc(candidate, reportTimeZone);
            var marketDate = TimeZoneInfo.ConvertTimeFromUtc(candidateUtc, marketTimeZone);
            if (marketDate.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                var delay = candidateUtc - observation.UtcDateTime;
                return delay < TimeSpan.Zero ? MinimumDelay : delay;
            }

            candidate = candidate.AddDays(1);
        }
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
