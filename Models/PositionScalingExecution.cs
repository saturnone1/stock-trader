namespace StockTrader.Models;

/// <summary>한 포지션에서 사용자 전략의 스케일링 규칙이 실제 체결된 횟수.</summary>
public sealed class PositionScalingExecution
{
    public long PositionId { get; set; }
    public int RuleIndex { get; set; }
    public int ExecutionCount { get; set; }
    public Position Position { get; set; } = null!;
}
