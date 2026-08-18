namespace StockTrader.Domain.Strategies;

public sealed record PatternDescriptor(
    PatternType Value,
    string Code,
    string DisplayName,
    bool IsBuiltIn);

/// <summary>
/// 전략 식별 코드와 투자자용 표시 이름, 내장 실행 지원 범위의 단일 원천입니다.
/// 저장 및 API 호환성을 위해 Code는 PatternType 이름을 그대로 사용합니다.
/// </summary>
public static class PatternCatalog
{
    public static IReadOnlyList<PatternDescriptor> All { get; } =
    [
        P(PatternType.GapUpPullback, "갭 상승 후 눌림목"),
        P(PatternType.Breakout, "가격 돌파"),
        P(PatternType.VwapReversion, "VWAP 회귀"),
        P(PatternType.RsiMeanReversion, "RSI 평균 회귀"),
        P(PatternType.TrendPullback, "추세 눌림목"),
        P(PatternType.OpeningRangeBreakout, "장 초반 범위 돌파"),
        P(PatternType.VolumeSpikeContinuation, "거래량 급증 추세 지속"),
        P(PatternType.EarningsDrift, "실적 발표 후 추세"),
        P(PatternType.IndexRegimeFilter, "시장 추세 필터"),
        P(PatternType.VolatilityExpansion, "변동성 확대"),
        P(PatternType.MomentumReversal, "모멘텀 반전"),
        P(PatternType.MultiTimeframeTrend, "다중 시간축 추세"),
        P(PatternType.MeanReversionChannel, "평균 회귀 채널"),
        P(PatternType.Rsi2Bollinger, "RSI(2)·볼린저 반등"),
        P(PatternType.VolatilityBreakout, "변동성 돌파"),
        P(PatternType.Tqqq200Sma, "TQQQ 200일 이동평균선"),
        P(PatternType.CumulativeRsi2, "누적 RSI(2)"),
        new(PatternType.Custom, nameof(PatternType.Custom), "사용자 전략", false)
    ];

    public static IReadOnlyList<PatternDescriptor> BuiltIn { get; } =
        All.Where(item => item.IsBuiltIn).ToArray();

    public static PatternDescriptor Get(PatternType patternType) =>
        All.Single(item => item.Value == patternType);

    public static bool IsBuiltIn(PatternType patternType) =>
        BuiltIn.Any(item => item.Value == patternType);

    public static string DisplayName(PatternType patternType, string? customPatternName = null) =>
        patternType == PatternType.Custom && !string.IsNullOrWhiteSpace(customPatternName)
            ? customPatternName.Trim()
            : Get(patternType).DisplayName;

    private static PatternDescriptor P(PatternType value, string displayName) =>
        new(value, value.ToString(), displayName, true);
}
