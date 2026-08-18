using StockTrader.Models;

namespace StockTrader.Application.Execution;

/// <summary>
/// 브로커에서 확인한 실제 체결 상태를 로컬 실시간 포지션으로 변환합니다.
/// 자동 주문과 수동 주문이 동일한 수량·평균단가·위험거리 기준을 사용하도록 하는
/// 단일 진입 경계입니다.
/// </summary>
public static class LiveEntryPositionFactory
{
    public static Position CreateFromFill(
        TradeRecommendation recommendation,
        int accountId,
        int filledQuantity,
        decimal averageFillPrice,
        DateTime filledAt)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        var fill = LongEntryFillPolicy.ReanchorExecutedFill(
            recommendation.EntryPrice,
            recommendation.StopLossPrice,
            recommendation.TargetPrice,
            averageFillPrice);

        return new Position
        {
            SourceSignalId = recommendation.SourceSignalId,
            AccountId = accountId,
            Symbol = recommendation.Symbol,
            Quantity = filledQuantity,
            InitialQuantity = filledQuantity,
            EntryPrice = averageFillPrice,
            CurrentPrice = averageFillPrice,
            StopLossPrice = fill.StopPrice,
            TargetPrice = fill.TargetPrice,
            PatternType = recommendation.PatternType,
            CustomPatternName = recommendation.CustomPatternName,
            OpenedAt = filledAt,
            HighSinceEntry = averageFillPrice,
            InitialRiskDistance = fill.RiskDistance,
        };
    }
}
