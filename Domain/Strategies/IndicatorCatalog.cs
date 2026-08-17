namespace StockTrader.Domain.Strategies;

/// <summary>
/// 전략 규칙에서 선택할 수 있는 기술 지표의 단일 메타데이터 원천이다.
/// 계산 구현은 Engine에 남기고, 코드·표시명·기본 입력값·필요 과거 봉은 여기서 관리한다.
/// </summary>
public sealed record IndicatorParameterDescriptor(
    string Key,
    string DisplayName,
    decimal DefaultValue,
    decimal Step = 1m,
    bool MustBePositive = true);

public sealed record IndicatorDescriptor(
    string Code,
    string DisplayName,
    string Category,
    string DefaultOperator,
    decimal DefaultThreshold,
    string? ValueGuide,
    IReadOnlyList<IndicatorParameterDescriptor> Parameters);

public static class IndicatorCatalog
{
    private static IndicatorParameterDescriptor P(
        string key, string name, decimal value, decimal step = 1m) => new(key, name, value, step);

    private static readonly IndicatorDescriptor[] Descriptors =
    [
        new("BREAKOUT_HIGH", "돌파 고점", "가격 구조", ">=", 1, "참=1", [P("period", "돌파 기준 기간", 20)]),
        new("BREAKOUT_LOW", "돌파 저점", "가격 구조", ">=", 1, "참=1", [P("period", "돌파 기준 기간", 20)]),
        new("DIST_FROM_HIGH", "고점 대비 거리", "가격 구조", "<=", 2, "고점 아래 거리 %", [P("period", "기준 고점 기간", 20)]),
        new("DIST_FROM_LOW", "저점 대비 거리", "가격 구조", ">=", 5, "저점 위 거리 %", [P("period", "기준 저점 기간", 20)]),
        new("GAP", "갭 상승", "가격 구조", ">=", 1.5m, "%", []),
        new("HIGHER_LOW", "저점 상승 지속", "가격 구조", ">=", 2, "연속 봉 수", []),
        new("LOWER_HIGH", "고점 하락 지속", "가격 구조", ">=", 2, "연속 봉 수", []),
        new("INSIDE_BAR", "인사이드 바", "가격 구조", ">=", 1, "참=1", []),
        new("ENGULFING", "장악형 캔들", "가격 구조", ">=", 1, "강세=1, 약세=-1", []),

        new("RSI", "RSI 과매도", "모멘텀", "<=", 30, "0~100", [P("period", "RSI 기간", 14)]),
        new("CUMULATIVE_RSI", "누적 RSI", "모멘텀", "<=", 10, "RSI 합계", [P("period", "RSI 기간", 2), P("cumulativePeriod", "누적 기간", 2)]),
        new("STOCHASTIC_K", "스토캐스틱 K", "모멘텀", "<=", 20, "0~100", [P("period", "스토캐스틱 기간", 14)]),
        new("STOCHASTIC_D", "스토캐스틱 D", "모멘텀", "<=", 20, "0~100", [P("period", "스토캐스틱 기간", 14), P("smooth", "평활 기간", 3)]),
        new("MACD_HIST", "MACD 히스토그램", "모멘텀", ">", 0, null, [P("fast", "빠른 EMA", 12), P("slow", "느린 EMA", 26), P("signal", "시그널 기간", 9)]),
        new("CONSECUTIVE_UP", "연속 상승", "모멘텀", ">=", 3, "연속 봉 수", []),
        new("CONSECUTIVE_DOWN", "연속 하락", "모멘텀", ">=", 3, "연속 봉 수", []),
        new("ADX", "ADX 추세", "모멘텀", ">=", 25, null, [P("period", "ADX 기간", 14)]),
        new("ROC", "ROC 모멘텀", "모멘텀", ">=", 5, "%", [P("period", "ROC 기간", 14)]),
        new("CCI", "CCI", "모멘텀", "<=", -100, null, [P("period", "CCI 기간", 20)]),
        new("WILLIAMS_R", "윌리엄스 %R", "모멘텀", "<=", -80, "-100~0", [P("period", "윌리엄스 %R 기간", 14)]),

        new("PRICE_VS_SMA", "가격 vs SMA", "추세/평균", ">", 0, "%", [P("period", "SMA 기간", 20)]),
        new("PRICE_VS_EMA", "가격 vs EMA", "추세/평균", ">", 0, "%", [P("period", "EMA 기간", 20)]),
        new("PRICE_VS_VWAP", "가격 vs VWAP", "추세/평균", ">", 0, "%", [P("period", "VWAP 기준 기간", 20)]),
        new("SMA_SLOPE", "SMA 기울기", "추세/평균", ">", 0, "%", [P("period", "SMA 기간", 20), P("lookback", "기울기 비교 봉 수", 5)]),
        new("OBV", "OBV 누적거래량", "추세/평균", ">", 0, null, []),
        new("OBV_SLOPE", "OBV 기울기", "추세/평균", ">", 0, "%", [P("lookback", "기울기 비교 봉 수", 5)]),
        new("VOLUME_RATIO", "거래량 비율", "추세/평균", ">=", 1.5m, "평균 대비 배수", [P("period", "평균 거래량 기간", 20)]),
        new("CMF", "CMF", "추세/평균", ">", 0, "-1~1", [P("period", "CMF 기간", 20)]),

        new("BOLLINGER_POS", "볼린저 위치", "변동성/기타", "<=", 0.1m, "0=하단, 1=상단", [P("period", "볼린저 기간", 20), P("stddev", "표준편차", 2, 0.1m)]),
        new("ATR", "ATR", "변동성/기타", ">=", 1, null, [P("period", "ATR 기간", 14)]),
        new("ATR_PERCENT", "ATR %", "변동성/기타", ">=", 2, "%", [P("period", "ATR 기간", 14)]),
        new("PRICE_CHANGE", "가격 변화율", "변동성/기타", ">=", 3, "%", [P("bars", "비교 봉 수", 5)]),
        new("VOLATILITY_20D", "20일 변동성", "변동성/기타", ">=", 30, "연환산 %", [P("period", "변동성 기간", 20)]),
        new("CANDLE_BODY", "캔들 몸통 비율", "변동성/기타", ">=", 0.6m, "0~1 비율", [])
    ];

    private static readonly IReadOnlyDictionary<string, IndicatorDescriptor> ByCode =
        Descriptors.ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<IndicatorDescriptor> All { get; } = Descriptors;

    public static bool Contains(string? code) =>
        !string.IsNullOrWhiteSpace(code) && ByCode.ContainsKey(code);

    public static IndicatorDescriptor Get(string code) =>
        ByCode.TryGetValue(code, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(code), code, "지원하지 않는 전략 지표입니다.");

    public static decimal ParameterDefault(string code, string key, decimal fallback) =>
        Get(code).Parameters.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.DefaultValue ?? fallback;

    public static int RequiredBars(string? code, IReadOnlyDictionary<string, decimal>? parameters)
    {
        if (string.IsNullOrWhiteSpace(code) || !Contains(code)) return 3;
        parameters ??= new Dictionary<string, decimal>();
        int V(string key, int fallback) => parameters.TryGetValue(key, out var value)
            ? (int)value
            : (int)ParameterDefault(code, key, fallback);

        return code.ToUpperInvariant() switch
        {
            "RSI" or "PRICE_VS_SMA" or "PRICE_VS_EMA" or "BOLLINGER_POS" or "VOLUME_RATIO"
                or "ATR" or "ATR_PERCENT" or "VOLATILITY_20D" or "PRICE_VS_VWAP"
                or "CCI" or "ROC" or "WILLIAMS_R" or "CMF" => V("period", 14) + 2,
            "CUMULATIVE_RSI" => V("period", 2) + V("cumulativePeriod", 2) + 2,
            "MACD_HIST" => V("slow", 26) + V("signal", 9) + 2,
            "PRICE_CHANGE" => V("bars", 1) + 2,
            "SMA_SLOPE" => V("period", 20) + V("lookback", 5) + 2,
            "DIST_FROM_HIGH" or "DIST_FROM_LOW" or "BREAKOUT_HIGH" or "BREAKOUT_LOW" => V("period", 20) + 2,
            "ADX" => V("period", 14) * 2 + 1,
            "STOCHASTIC_K" => V("period", 14) + 2,
            "STOCHASTIC_D" => V("period", 14) + V("smooth", 3) + 2,
            "OBV_SLOPE" => V("lookback", 5) + 2,
            _ => 3
        };
    }
}
