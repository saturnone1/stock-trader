namespace StockTrader.Domain.Strategies;

public sealed record StrategyOptionDescriptor(string Code, string DisplayName);

public sealed record ExitMethodDescriptor(
    string Code,
    string DisplayName,
    IReadOnlyList<IndicatorParameterDescriptor> Parameters);

/// <summary>
/// 전략 구성 화면과 실행 검증이 공유하는 선택값의 단일 원천이다.
/// 저장 호환성을 위해 Code는 기존 문자열 값을 그대로 유지한다.
/// </summary>
public static class StrategyCatalog
{
    public const string FixedRiskSizingMode = "FixedRisk";
    public const string KellySizingMode = "Kelly";
    public const string HalfKellySizingMode = "HalfKelly";
    public const string ScalingInDirection = "SCALE_IN";
    public const string ScalingOutDirection = "SCALE_OUT";

    private static IndicatorParameterDescriptor P(
        string key, string name, decimal value, decimal step = 1m) => new(key, name, value, step);

    public static IReadOnlyList<StrategyOptionDescriptor> EntryModes { get; } =
    [
        new("CurrentClose", "신호 봉의 종가에 매수"),
        new("NextOpen", "다음 봉의 시가에 매수")
    ];

    public static IReadOnlyList<StrategyOptionDescriptor> SizingModes { get; } =
    [
        new(FixedRiskSizingMode, "손실 허용액 기준"),
        new(KellySizingMode, "켈리 공식"),
        new(HalfKellySizingMode, "절반 켈리 공식")
    ];

    public static IReadOnlyList<StrategyOptionDescriptor> LogicModes { get; } =
    [
        new("AND", "모두 만족"),
        new("OR", "하나만 만족")
    ];

    public static IReadOnlyList<StrategyOptionDescriptor> ScalingDirections { get; } =
    [
        new(ScalingInDirection, "추가 매수"),
        new(ScalingOutDirection, "일부 매도")
    ];

    public static IReadOnlyList<ExitMethodDescriptor> StopMethods { get; } =
    [
        new("ATR", "ATR 기준", [P("multiplier", "ATR 배수", 2, 0.1m), P("period", "ATR 기간", 14)]),
        new("BOLLINGER_LOWER", "볼린저 하단", [P("period", "기간", 20), P("stddev", "표준편차", 2, 0.1m)]),
        new("SMA", "단순이동평균", [P("period", "기간", 20)]),
        new("EMA", "지수이동평균", [P("period", "기간", 20)]),
        new("PREV_LOW", "이전 저점", [P("period", "되돌아보기", 5)]),
        new("PERCENT", "퍼센트 기준", [P("percent", "퍼센트", 2, 0.1m)])
    ];

    public static IReadOnlyList<ExitMethodDescriptor> TargetMethods { get; } =
    [
        new("ATR", "ATR 기준", [P("multiplier", "ATR 배수", 3, 0.1m), P("period", "ATR 기간", 14)]),
        new("BOLLINGER_UPPER", "볼린저 상단", [P("period", "기간", 20), P("stddev", "표준편차", 2, 0.1m)]),
        new("SMA", "단순이동평균", [P("period", "기간", 20)]),
        new("EMA", "지수이동평균", [P("period", "기간", 20)]),
        new("PREV_HIGH", "이전 고점", [P("period", "되돌아보기", 5)]),
        new("R_MULTIPLE", "R 배수", [P("multiple", "R 배수", 3, 0.1m)]),
        new("PERCENT", "퍼센트 기준", [P("percent", "퍼센트", 5, 0.1m)])
    ];

    public static bool IsEntryMode(string? value) => Contains(EntryModes, value);
    public static bool IsSizingMode(string? value) => Contains(SizingModes, value);
    public static bool IsLogicMode(string? value) => Contains(LogicModes, value);
    public static bool IsScalingDirection(string? value) => Contains(ScalingDirections, value);
    public static bool IsStopMethod(string? value) => Contains(StopMethods, value);
    public static bool IsTargetMethod(string? value) => Contains(TargetMethods, value);

    private static bool Contains(IEnumerable<StrategyOptionDescriptor> options, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && options.Any(item => item.Code.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool Contains(IEnumerable<ExitMethodDescriptor> options, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && options.Any(item => item.Code.Equals(value, StringComparison.OrdinalIgnoreCase));
}
