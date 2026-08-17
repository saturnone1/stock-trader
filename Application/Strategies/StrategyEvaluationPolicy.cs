namespace StockTrader.Application.Strategies;

/// <summary>모든 전략 실행 경로가 공유하는 데이터 평가 전제입니다.</summary>
public static class StrategyEvaluationPolicy
{
    /// <summary>지표 워밍업과 첫 신호 평가 전에 확보해야 하는 최소 봉 수입니다.</summary>
    public const int MinimumWarmupBars = 50;
}
