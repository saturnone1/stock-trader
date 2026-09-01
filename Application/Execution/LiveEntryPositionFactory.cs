using StockTrader.Models;
using System.Text.Json;

namespace StockTrader.Application.Execution;

/// <summary>
/// 브로커에서 확인한 실제 체결 상태를 로컬 실시간 포지션으로 변환합니다.
/// 자동 주문과 수동 주문이 동일한 수량·평균단가·위험거리 기준을 사용하도록 하는
/// 단일 진입 경계입니다.
/// </summary>
public static class LiveEntryPositionFactory
{
    private static readonly JsonSerializerOptions ContractJson =
        new(JsonSerializerDefaults.Web);

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

        var evidence = recommendation.MarketDataEvidence;
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
            ExecutionArtifactJson = recommendation.ExecutionArtifact is null
                ? null
                : JsonSerializer.Serialize(recommendation.ExecutionArtifact, ContractJson),
            EntryMarketDataEvidenceJson = evidence is null
                ? null
                : JsonSerializer.Serialize(evidence, ContractJson),
            LastEvaluatedEvidenceId = evidence?.EvidenceId,
            LastEvaluatedBarUtc = evidence?.LastBarUtc,
            LastEvaluatedMarketDataRevision = evidence?.Revision ?? 0,
        };
    }
}
