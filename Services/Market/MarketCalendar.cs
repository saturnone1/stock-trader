using Microsoft.Extensions.Logging;
using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;
using TimeZoneConverter;

namespace StockTrader.Services.Market;

/// <summary>
/// US(NYSE/NASDAQ)와 KRX 시장 시간을 통합 관리하는 캘린더 서비스.
/// 주말·거래소 휴장일·조기마감 근거는 <see cref="ExchangeCalendarCatalog"/> 가 소유하며,
/// 이 어댑터는 시간대 변환과 현재 시각 적용만 담당한다.
/// </summary>
public class MarketCalendar : IMarketCalendar
{
    private static readonly IReadOnlyDictionary<MarketRegion, TimeZoneInfo> TimeZones =
        MarketRegionCatalog.All.ToDictionary(
            item => item.Value,
            item => TZConvert.GetTimeZoneInfo(item.TimeZoneId));

    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarketCalendar> _logger;

    public MarketCalendar(TimeProvider timeProvider, ILogger<MarketCalendar> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool IsMarketOpen(MarketRegion market)
    {
        var now = GetLocalNow(market);

        TradingDayEvidence evidence;
        try
        {
            evidence = GetTradingDay(market, DateOnly.FromDateTime(now));
        }
        catch (MarketCalendarCoverageException ex)
        {
            // 실거래 게이트는 근거가 없을 때 닫힌 상태로 실패한다. 휴장일 수도 있는 날에
            // 주문을 허용하는 것보다, 캘린더를 갱신할 때까지 거래를 멈추는 편이 안전하다.
            _logger.LogError(ex,
                "[MarketCalendar] {Market} 캘린더 근거 없음 → 장 닫힘으로 처리. 캘린더 갱신 필요", market);
            return false;
        }

        if (!evidence.IsTradingDay)
            return false;

        var open = GetMarketOpen(market);
        // 조기마감일은 정규 종료시각이 아니라 그날의 실제 마감시각을 경계로 삼는다.
        var close = evidence.EarlyCloseTime ?? GetMarketClose(market);
        return now.TimeOfDay >= open && now.TimeOfDay <= close;
    }

    public TradingDayEvidence GetTradingDay(MarketRegion market, DateOnly marketDate) =>
        ExchangeCalendarCatalog.GetTradingDay(market, marketDate);

    public TimeZoneInfo GetTimeZone(MarketRegion market) =>
        TimeZones.TryGetValue(market, out var timeZone)
            ? timeZone
            : throw new ArgumentOutOfRangeException(
                nameof(market), market, "지원하지 않는 시장입니다.");

    public TimeSpan GetMarketOpen(MarketRegion market) =>
        MarketRegionCatalog.Get(market).RegularOpen;

    public TimeSpan GetMarketClose(MarketRegion market) =>
        MarketRegionCatalog.Get(market).RegularClose;

    public DateTime GetLocalNow(MarketRegion market) =>
        GetLocalTime(market, _timeProvider.GetUtcNow().UtcDateTime);

    public DateTime GetLocalTime(MarketRegion market, DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            GetTimeZone(market));

}
