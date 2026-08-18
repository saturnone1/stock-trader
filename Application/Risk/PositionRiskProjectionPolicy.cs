namespace StockTrader.Application.Risk;

public sealed record PositionRiskMetrics(
    decimal RiskPerShare,
    decimal RMultiple,
    int HoldingDays);

/// <summary>포지션의 최초 위험 대비 현재 성과와 보유 기간을 결정적으로 계산합니다.</summary>
public static class PositionRiskProjectionPolicy
{
    public static PositionRiskMetrics Evaluate(
        decimal entryPrice,
        decimal currentPrice,
        decimal stopLossPrice,
        DateTime openedAt,
        DateTime observedAt)
    {
        var riskPerShare = entryPrice - stopLossPrice;
        var rMultiple = riskPerShare != 0m
            ? (currentPrice - entryPrice) / Math.Abs(riskPerShare)
            : 0m;
        var holdingDays = Math.Max(0, (observedAt - openedAt).Days);
        return new PositionRiskMetrics(riskPerShare, rMultiple, holdingDays);
    }
}

/// <summary>거래 중단 알림의 재전송 간격을 시스템 시간과 분리합니다.</summary>
public static class RiskAlertPolicy
{
    public static bool IsDue(
        bool isTradingHalted,
        DateTime lastAlertAt,
        DateTime observedAt,
        TimeSpan minimumInterval) =>
        isTradingHalted
        && minimumInterval > TimeSpan.Zero
        && observedAt >= lastAlertAt
        && observedAt - lastAlertAt >= minimumInterval;
}
