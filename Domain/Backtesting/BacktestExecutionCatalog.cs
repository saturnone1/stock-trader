namespace StockTrader.Domain.Backtesting;

/// <summary>
/// 백테스트에서 체결 가격의 불확실성을 반영하는 방식이다.
/// </summary>
public enum SlippageModel
{
    Fixed,
    Adaptive
}

public sealed record SlippageModelDescriptor(
    SlippageModel Value,
    string DisplayName,
    string Description,
    bool IsDefault = false);

/// <summary>
/// 백테스트 실행 엔진과 화면이 공유하는 체결 비용 정책의 단일 원천이다.
/// </summary>
public static class BacktestExecutionCatalog
{
    public const SlippageModel DefaultSlippageModel = SlippageModel.Adaptive;

    public static IReadOnlyList<SlippageModelDescriptor> SlippageModels { get; } =
    [
        new(
            SlippageModel.Adaptive,
            "시장 상황 반영 (권장)",
            "ATR 변동성과 주문 규모 대비 거래량을 반영해 체결 가격 불리함을 계산합니다.",
            IsDefault: true),
        new(
            SlippageModel.Fixed,
            "고정 비율",
            "모든 거래에 입력한 슬리피지 비율을 동일하게 적용합니다.")
    ];
}
