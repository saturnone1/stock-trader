using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Execution;

public sealed record PositionSizingTradeSample(decimal Pnl, decimal ReturnFraction);

public sealed record LongPositionSizingRequest(
    decimal AccountEquity,
    decimal RiskFraction,
    decimal EntryPrice,
    decimal StopPrice,
    int MaxTotalPositions,
    decimal MaxSinglePositionPercent = 0m);

public sealed record LongPositionSizingDecision(
    int Quantity,
    decimal PositionCapital,
    decimal RiskCapital,
    decimal PositionCapFraction)
{
    public bool CanEnter => Quantity > 0;
}

/// <summary>
/// 실시간 추천과 백테스트가 공유하는 Kelly 리스크 및 손절거리 기반 매수 수량 정책입니다.
/// 모든 비율은 0.01 = 1%인 소수 비율을 사용합니다.
/// </summary>
public static class LongPositionSizingPolicy
{
    public const int MinimumKellySamples = 10;
    public const decimal MaximumKellyFraction = 0.25m;

    public static decimal ResolveRiskFraction(
        decimal baseRiskFraction,
        string? sizingMode,
        IReadOnlyCollection<PositionSizingTradeSample> completedTrades)
    {
        if (completedTrades.Count < MinimumKellySamples
            || sizingMode is not (StrategyCatalog.KellySizingMode or StrategyCatalog.HalfKellySizingMode))
        {
            return baseRiskFraction;
        }

        var wins = completedTrades.Where(trade => trade.Pnl > 0).ToList();
        var losses = completedTrades.Where(trade => trade.Pnl < 0).ToList();
        var winRate = (decimal)wins.Count / completedTrades.Count;
        var averageWin = wins.Count > 0 ? wins.Average(trade => trade.ReturnFraction) : 0;
        var averageLoss = losses.Count > 0
            ? Math.Abs(losses.Average(trade => trade.ReturnFraction))
            : 0;
        var kelly = ComputeKellyFraction(winRate, averageWin, averageLoss);
        if (kelly <= 0) return baseRiskFraction;

        return sizingMode == StrategyCatalog.HalfKellySizingMode ? kelly / 2 : kelly;
    }

    public static decimal ComputeKellyFraction(
        decimal winRate,
        decimal averageWin,
        decimal averageLoss)
    {
        if (averageWin <= 0 || averageLoss <= 0) return 0;

        var payoffRatio = averageWin / averageLoss;
        if (payoffRatio <= 0) return 0;

        var kelly = winRate - (1 - winRate) / payoffRatio;
        return Math.Clamp(kelly, 0m, MaximumKellyFraction);
    }

    public static decimal CalculateRiskCapital(
        decimal accountEquity,
        decimal riskFraction,
        decimal entryPrice,
        decimal stopPrice)
    {
        if (accountEquity <= 0 || riskFraction <= 0 || entryPrice <= 0 || stopPrice <= 0)
            return 0;

        var stopFraction = Math.Abs(entryPrice - stopPrice) / entryPrice;
        return stopFraction > 0 ? accountEquity * riskFraction / stopFraction : 0;
    }

    public static LongPositionSizingDecision Calculate(LongPositionSizingRequest request)
    {
        var riskCapital = CalculateRiskCapital(
            request.AccountEquity,
            request.RiskFraction,
            request.EntryPrice,
            request.StopPrice);
        if (riskCapital <= 0)
            return new LongPositionSizingDecision(0, 0, 0, 0);

        var capFraction = request.MaxTotalPositions > 0
            ? 1m / request.MaxTotalPositions
            : 0.10m;
        if (request.MaxSinglePositionPercent > 0)
            capFraction = Math.Min(capFraction, request.MaxSinglePositionPercent / 100m);

        var cappedCapital = Math.Min(riskCapital, request.AccountEquity * capFraction);
        var quantity = request.EntryPrice > 0
            ? (int)Math.Floor(cappedCapital / request.EntryPrice)
            : 0;
        var positionCapital = quantity > 0 ? quantity * request.EntryPrice : 0;

        return new LongPositionSizingDecision(
            quantity, positionCapital, riskCapital, capFraction);
    }
}
