using StockTrader.Domain.MarketData;

namespace StockTrader.Application.MarketData;

/// <summary>
/// 주기 작업 예약에서 쓰는 거래일 판정.
///
/// 주문 게이트(<see cref="IMarketCalendar.IsMarketOpen"/>)는 근거가 없으면 닫힌 상태로
/// 실패해야 하지만, 예약은 반대 방향으로 안전하다. 캘린더가 다루지 않는 날짜를 휴장일로
/// 간주하면 보고·재학습·쿨다운 만료가 무기한 미뤄지기 때문이다. 이 경로들은 주문을 내지
/// 않고 이미 확정된 이력을 읽거나 일정을 계산할 뿐이므로, 근거가 없을 때는 거래일로 세고
/// 실제 주문 여부는 별도의 닫힘 판정에 맡긴다.
/// </summary>
public static class MarketCalendarSchedulingExtensions
{
    public static bool IsTradingDayForScheduling(
        this IMarketCalendar calendar, MarketRegion market, DateOnly date)
    {
        try
        {
            return calendar.GetTradingDay(market, date).IsTradingDay;
        }
        catch (MarketCalendarCoverageException)
        {
            return true;
        }
    }

    /// <summary>해당 시장의 거래일 판정을 정책에 넘길 수 있는 술어로 만든다.</summary>
    public static Func<DateOnly, bool> TradingDayPredicate(
        this IMarketCalendar calendar, MarketRegion market) =>
        date => calendar.IsTradingDayForScheduling(market, date);
}
