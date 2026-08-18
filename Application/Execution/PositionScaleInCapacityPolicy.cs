namespace StockTrader.Application.Execution;

/// <summary>
/// 포트폴리오 한도 안에서 한 롱 포지션이 사용할 수 있는 최대 원금을 계산합니다.
/// 백테스트와 실시간 어댑터는 자본·전역 보유 한도·전략 비중만 투영합니다.
/// </summary>
public static class PositionScaleInCapacityPolicy
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
