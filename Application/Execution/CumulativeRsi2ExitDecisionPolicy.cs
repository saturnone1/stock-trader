namespace StockTrader.Application.Execution;

/// <summary>
/// 누적 RSI2 전략의 종가 기반 매도 판단을 정의합니다.
/// 장기 추세선 이탈을 누적 RSI 임계값보다 우선합니다.
/// </summary>
public static class CumulativeRsi2ExitDecisionPolicy
{
    public static StrategyExitInstruction? Resolve(
        decimal currentPrice,
        decimal cumulativeRsi2,
        decimal longTrendMovingAverage,
        decimal exitThreshold,
        int longTrendMovingAveragePeriod)
    {
        if (currentPrice <= 0)
            return null;

        if (longTrendMovingAverage > 0 && currentPrice <= longTrendMovingAverage)
        {
            return new StrategyExitInstruction(
                currentPrice,
                $"{longTrendMovingAveragePeriod}SMA 이탈");
        }

        if (cumulativeRsi2 >= exitThreshold)
        {
            return new StrategyExitInstruction(
                currentPrice,
                $"누적 RSI 청산({cumulativeRsi2:F1})");
        }

        return null;
    }
}
