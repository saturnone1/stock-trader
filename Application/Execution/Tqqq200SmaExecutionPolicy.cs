namespace StockTrader.Application.Execution;

public sealed record TqqqEntryLevels(decimal StopPrice, decimal TargetPrice);

/// <summary>
/// TQQQ 장기 추세선 전략의 진입 가격대와 보유 중 보호 손절을 한 곳에서 계산합니다.
/// 감지기·백테스트·실시간 실행은 같은 입력으로 이 정책을 사용해야 합니다.
/// </summary>
public static class Tqqq200SmaExecutionPolicy
{
    public static TqqqEntryLevels? ResolveEntryLevels(
        decimal entryPrice,
        decimal trendSma,
        decimal fixedStopPercent,
        decimal smaStopMultiplier,
        decimal targetSmaMultiplier,
        decimal minimumTargetReturnPercent)
    {
        if (entryPrice <= 0 || trendSma <= 0
            || fixedStopPercent < 0 || fixedStopPercent >= 1
            || !IsValidExecutionConfiguration(
                smaStopMultiplier, targetSmaMultiplier, minimumTargetReturnPercent))
            return null;

        var fixedStop = entryPrice * (1m - fixedStopPercent);
        var trendStop = ResolveProtectiveStopFloor(trendSma, smaStopMultiplier);
        if (!trendStop.HasValue)
            return null;

        var target = trendSma * targetSmaMultiplier;
        if (target <= entryPrice)
            target = entryPrice * (1m + minimumTargetReturnPercent);

        var stop = Math.Max(fixedStop, trendStop.Value);
        if (stop >= entryPrice || target <= entryPrice)
            return null;

        return new TqqqEntryLevels(stop, target);
    }

    public static bool IsValidTrendStopConfiguration(int smaPeriod, decimal smaStopMultiplier) =>
        smaPeriod > 0 && smaStopMultiplier > 0;

    public static bool IsValidExecutionConfiguration(
        decimal smaStopMultiplier,
        decimal targetSmaMultiplier,
        decimal minimumTargetReturnPercent) =>
        smaStopMultiplier > 0
        && targetSmaMultiplier > 0
        && minimumTargetReturnPercent >= 0;

    public static decimal? ResolveProtectiveStopFloor(decimal trendSma, decimal smaStopMultiplier) =>
        trendSma > 0 && smaStopMultiplier > 0
            ? trendSma * smaStopMultiplier
            : null;

    /// <summary>미국 거래일 기준 SMA를 안정적으로 확보하기 위한 보수적 달력 조회 기간입니다.</summary>
    public static int RequiredCalendarLookbackDays(int smaPeriod)
    {
        if (smaPeriod <= 0)
            return 0;

        const int calendarDaysPerTradingDayNumerator = 7;
        const int tradingDaysPerWeek = 5;
        const int holidayAndFeedBufferDays = 30;
        return (int)Math.Ceiling(smaPeriod * (decimal)calendarDaysPerTradingDayNumerator / tradingDaysPerWeek)
               + holidayAndFeedBufferDays;
    }
}
