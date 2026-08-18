namespace StockTrader.Application.Backtesting;

/// <summary>
/// 백테스트 포트폴리오 한도 안에서 한 포지션이 사용할 수 있는 최대 원금을 계산합니다.
/// 실행 처리기는 포트폴리오와 전략 설정을 원시 값으로 투영하고 이 정책을 공유합니다.
/// </summary>
public static class BacktestScaleInCapacityPolicy
{
    public static decimal CalculateMaxPositionCost(
        decimal currentEquity,
        int maxTotalPositions,
        decimal strategyMaxSinglePositionPercent)
    {
        if (currentEquity <= 0)
            return 0m;

        var capFraction = maxTotalPositions > 0
            ? 1m / maxTotalPositions
            : 0.10m;
        if (strategyMaxSinglePositionPercent > 0)
        {
            capFraction = Math.Min(
                capFraction,
                strategyMaxSinglePositionPercent / 100m);
        }

        return currentEquity * capFraction;
    }
}
