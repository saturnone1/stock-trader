namespace StockTrader.Application.Execution;

/// <summary>
/// 전략 실행 정책이 사용하는 저장소 독립적인 완료 거래 관측값입니다.
/// </summary>
public sealed record StrategyCompletedTrade(
    long SequenceId,
    DateTime ExitedAt,
    decimal RealizedPnl,
    decimal ReturnFraction);
