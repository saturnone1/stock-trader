using StockTrader.Domain.MarketData;

namespace StockTrader.Application.MarketData;

/// <summary>
/// 하나의 준비·실행이 어떤 조건에서 수행되었는지에 대한 명시적 진술.
///
/// 이 값들은 이전에는 어댑터 내부의 암묵적 가정이었다. 결과만 보고서는 어떤 조정 계열을
/// 사용했는지, 어느 시장의 어느 세션 범위였는지, 워밍업이 실제로 충족되었는지 알 수 없었고,
/// 따라서 두 결과가 비교 가능한지도 증명할 수 없었다. 준비 단계가 이 근거를 구성하고
/// 결과 메타데이터가 그대로 보존하므로, 저장된 결과는 나중에 다시 읽어도 조건을 진술한다.
/// </summary>
public sealed record MarketDataEvidence(
    DataSource Provider,
    MarketRegion MarketRegion,
    string MarketTimeZoneId,
    TimeFrame TimeFrame,
    PriceAdjustmentMode AdjustmentMode,
    MarketSessionScope SessionScope,
    string CalendarVersion,
    int WarmupCalendarDays,
    int RequiredWarmupBars)
{
    /// <summary>공급자·타임프레임으로부터 카탈로그가 소유한 사실들을 조립한다.</summary>
    public static MarketDataEvidence Create(
        DataSource provider,
        TimeFrame timeFrame,
        MarketSessionScope sessionScope,
        int warmupCalendarDays,
        int requiredWarmupBars)
    {
        var descriptor = DataProviderCatalog.Get(provider);
        var market = MarketRegionCatalog.Get(descriptor.MarketRegion);

        return new MarketDataEvidence(
            provider,
            descriptor.MarketRegion,
            market.TimeZoneId,
            timeFrame,
            PriceAdjustmentCatalog.Resolve(provider, timeFrame),
            sessionScope,
            MarketCalendarVersion.Current,
            warmupCalendarDays,
            requiredWarmupBars);
    }

    /// <summary>
    /// 두 결과가 같은 데이터 조건에서 산출되어 비교 가능한지 여부.
    /// 조정 모드나 세션 범위가 다르면 수익률을 직접 비교할 수 없다.
    /// </summary>
    public bool IsComparableTo(MarketDataEvidence other) =>
        AdjustmentMode == other.AdjustmentMode
        && SessionScope == other.SessionScope
        && TimeFrame == other.TimeFrame
        && MarketRegion == other.MarketRegion
        && CalendarVersion == other.CalendarVersion;
}

/// <summary>가격 계열이 어느 거래 세션 구간을 포함하는지에 대한 명시적 범위.</summary>
public enum MarketSessionScope
{
    /// <summary>정규장만. 프리마켓·애프터마켓 체결 제외.</summary>
    RegularSessionOnly,

    /// <summary>정규장에 시간외 세션을 포함.</summary>
    IncludingExtendedHours
}
