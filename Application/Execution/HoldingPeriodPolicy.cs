using StockTrader.Models;

namespace StockTrader.Application.Execution;

public static class HoldingPeriodPolicy
{
    /// <summary>
    /// 진입일 이후 실제로 존재하는 완료 일봉 수로 최대 보유기간을 판단한다.
    /// 주말·휴일을 달력 비율로 추정하지 않는다.
    /// </summary>
    public static bool HasReachedDailyBarLimit(
        DateTime openedAt,
        IReadOnlyCollection<OhlcvBar> bars,
        int maxHoldingBars)
    {
        if (maxHoldingBars <= 0)
            return false;

        var entryDate = openedAt.Date;
        var completedBars = bars
            .Where(bar => bar.Timestamp.Date > entryDate)
            .Select(bar => bar.Timestamp.Date)
            .Distinct()
            .Count();
        return completedBars >= maxHoldingBars;
    }
}
