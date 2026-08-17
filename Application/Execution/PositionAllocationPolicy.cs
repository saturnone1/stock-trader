using StockTrader.Models;

namespace StockTrader.Application.Execution;

public sealed record PositionAllocationDecision(
    decimal EffectiveEquity,
    decimal RegimeScale,
    decimal AllocationScale)
{
    public int ReductionCount =>
        (RegimeScale < 1m ? 1 : 0) + (AllocationScale < 1m ? 1 : 0);
}

/// <summary>레짐 비중과 전략 비중 단계를 동일한 0~1 배수 의미로 적용합니다.</summary>
public static class PositionAllocationPolicy
{
    public static decimal NormalizeScale(decimal scale) => scale is > 0m and <= 1m ? scale : 1m;

    public static decimal ResolveRegimeScale(MarketRegime regime, WeightStrategy strategy)
    {
        if (regime.Spy200Ma <= 0) return 1m;
        if (!regime.SpyAbove200Ma) return NormalizeScale(strategy.BearWeight);

        var ratio = regime.SpyPrice / regime.Spy200Ma;
        if (ratio >= strategy.OverheatStage2Pct)
            return NormalizeScale(strategy.Overheat2Weight);
        if (ratio >= strategy.OverheatStage1Pct)
            return NormalizeScale(strategy.Overheat1Weight);
        return NormalizeScale(strategy.BullWeight);
    }

    public static PositionAllocationDecision Apply(
        decimal baseEquity,
        decimal regimeScale,
        decimal allocationScale)
    {
        var normalizedRegime = NormalizeScale(regimeScale);
        var normalizedAllocation = NormalizeScale(allocationScale);
        return new PositionAllocationDecision(
            Math.Max(0m, baseEquity) * normalizedRegime * normalizedAllocation,
            normalizedRegime,
            normalizedAllocation);
    }
}
