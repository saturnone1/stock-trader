using StockTrader.Models;

namespace StockTrader.Application.Execution;

/// <summary>
/// 브로커에서 확인한 실제 체결 상태를 로컬 실시간 포지션으로 변환합니다.
/// 자동 주문과 수동 주문이 동일한 수량·평균단가·위험거리 기준을 사용하도록 하는
/// 단일 진입 경계입니다.
/// </summary>
public static class LiveEntryPositionFactory
{
    public static Position Create(
        TradeRecommendation recommendation,
        Position? brokerPosition,
        int accountId,
        DateTime openedAt)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        var actualEntry = brokerPosition?.EntryPrice > 0
            ? brokerPosition.EntryPrice
            : recommendation.EntryPrice;
        var actualQuantity = brokerPosition?.Quantity > 0
            ? brokerPosition.Quantity
            : recommendation.ShareQuantity;
        var currentPrice = brokerPosition?.CurrentPrice > 0
            ? brokerPosition.CurrentPrice
            : actualEntry;
        var fill = LongEntryFillPolicy.ReanchorExecutedFill(
            recommendation.EntryPrice,
            recommendation.StopLossPrice,
            recommendation.TargetPrice,
            actualEntry);

        return new Position
        {
            AccountId = accountId,
            Symbol = recommendation.Symbol,
            Quantity = actualQuantity,
            EntryPrice = actualEntry,
            CurrentPrice = currentPrice,
            StopLossPrice = fill.StopPrice,
            TargetPrice = fill.TargetPrice,
            PatternType = recommendation.PatternType,
            CustomPatternName = recommendation.CustomPatternName,
            OpenedAt = openedAt,
            HighSinceEntry = actualEntry,
            InitialRiskDistance = fill.RiskDistance,
        };
    }
}
