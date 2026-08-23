namespace StockTrader.Application.Execution;

public sealed record LongPositionExecutionState(
    decimal EntryPrice,
    decimal StopPrice,
    decimal TargetPrice,
    decimal HighestPrice,
    decimal LowestPrice,
    decimal RiskDistance,
    decimal EntryAtr,
    int EntryBarIndex,
    int CurrentQuantity,
    bool PartialProfitTaken = false,
    bool BreakevenApplied = false,
    bool TrailingActivated = false);

public sealed record LongPositionExitPolicy(
    int MaxHoldingBars,
    bool EnableTrailingStop,
    decimal TrailingStopAtrMultiplier,
    decimal TrailingActivationR,
    bool EnablePartialProfit,
    decimal PartialProfitRMultiple,
    bool EnableTargetExit,
    bool EnableTimeExit,
    decimal BreakevenAtrMultiplier = 1.5m,
    string StopReason = "손절",
    string ProtectedStopReason = "트레일링 손절");

public enum PositionExecutionEventType
{
    PartialExit,
    Exit,
    StopMoved,
}

public sealed record PositionExecutionEvent(
    PositionExecutionEventType Type,
    decimal Price,
    int Quantity,
    string Reason);

public sealed record StrategyExitInstruction(decimal Price, string Reason);

public static class LongPositionExecutionReasons
{
    public const string StrategyRuleExit = "청산 규칙 충족";
}

public sealed record LongPositionBarResult(
    LongPositionExecutionState State,
    IReadOnlyList<PositionExecutionEvent> Events,
    bool IsClosed);

/// <summary>
/// 롱 포지션의 한 봉 체결 순서를 정의하는 순수 정책입니다.
/// OHLC만으로 장중 순서를 알 수 없으므로 기존 손절 → 부분 익절 → 목표/전략/시간 청산 →
/// 다음 봉 보호 손절 갱신 순으로 보수적으로 평가합니다.
/// </summary>
