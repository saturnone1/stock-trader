namespace StockTrader.Domain.MarketData;

/// <summary>
/// 가격 계열이 분할·배당을 반영했는지에 대한 명시적 근거.
/// 조정 여부가 다른 두 계열은 같은 종목이라도 서로 다른 수익률을 만들므로,
/// 준비·실행 입력과 결과 메타데이터는 어느 쪽을 사용했는지 진술해야 한다.
/// </summary>
public enum PriceAdjustmentMode
{
    /// <summary>분할·배당이 모두 반영되어 과거 구간이 현재 기준으로 재계산된 연속 계열.</summary>
    SplitsAndDividends,

    /// <summary>당시 실제 체결가. 분할·배당 미반영이므로 이벤트 시점에 불연속이 발생한다.</summary>
    Unadjusted
}

/// <summary>
/// 각 공급자 어댑터가 타임프레임별로 실제 전달하는 조정 모드의 단일 원천.
/// 어댑터 구현이 요청 파라미터를 바꾸면 이 카탈로그도 함께 바뀌어야 하며,
/// 특성화 테스트가 둘의 일치를 강제한다.
/// </summary>
public static class PriceAdjustmentCatalog
{
    private static readonly IReadOnlyDictionary<DataSource, PriceAdjustmentMode> DefaultModes =
        new Dictionary<DataSource, PriceAdjustmentMode>
        {
            // Alpaca: 일봉·분봉 모두 Adjustment.SplitsAndDividends 요청.
            [DataSource.Alpaca] = PriceAdjustmentMode.SplitsAndDividends,
            // Yahoo: adjclose 비율을 OHLC에 적용하여 항상 조정 계열로 정규화.
            [DataSource.Yahoo] = PriceAdjustmentMode.SplitsAndDividends,
            // LS증권: 일/주봉은 t8410 sujung="Y"로 수정주가 요청.
            [DataSource.LsSecurities] = PriceAdjustmentMode.SplitsAndDividends,
            // Polygon: 어댑터 미구현.
            [DataSource.Polygon] = PriceAdjustmentMode.SplitsAndDividends
        };

    /// <summary>
    /// 기본 모드와 다른 (공급자, 타임프레임) 조합.
    /// LS증권 분봉 TR(t8412)에는 수정주가 파라미터가 없어 원본 체결가가 반환된다.
    /// </summary>
    private static readonly IReadOnlyDictionary<(DataSource, TimeFrame), PriceAdjustmentMode> Overrides =
        new Dictionary<(DataSource, TimeFrame), PriceAdjustmentMode>
        {
            [(DataSource.LsSecurities, TimeFrame.OneMinute)] = PriceAdjustmentMode.Unadjusted,
            [(DataSource.LsSecurities, TimeFrame.FiveMinute)] = PriceAdjustmentMode.Unadjusted,
            [(DataSource.LsSecurities, TimeFrame.FifteenMinute)] = PriceAdjustmentMode.Unadjusted
        };

    /// <summary>해당 공급자·타임프레임 조합이 전달하는 조정 모드.</summary>
    public static PriceAdjustmentMode Resolve(DataSource provider, TimeFrame timeFrame)
    {
        if (Overrides.TryGetValue((provider, timeFrame), out var overridden))
            return overridden;

        return DefaultModes.TryGetValue(provider, out var mode)
            ? mode
            : throw new ArgumentOutOfRangeException(
                nameof(provider), provider, "조정 모드가 선언되지 않은 데이터 공급자입니다.");
    }
}
