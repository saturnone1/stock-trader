using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public static class TradingEntrySettlementPolicy
{
    public static TradingPositionProjection CreateFilledPosition(
        TradingEntryIntent intent,
        Broker.BrokerOrderEvidence evidence)
    {
        if (!string.Equals(evidence.Status, "Filled", StringComparison.Ordinal)
            || evidence.FilledQuantity != intent.ShareQuantity
            || evidence.AverageFillPrice is not > 0m
            || evidence.FilledAtUtc is null)
            throw new ArgumentException("Broker evidence is not a complete entry fill.", nameof(evidence));
        var fillPrice = evidence.AverageFillPrice.Value;
        return new TradingPositionProjection(
            PositionId(intent.Envelope.CommandId), intent.SourceSignalId, intent.AccountId,
            intent.Symbol, intent.Sector, evidence.FilledQuantity, evidence.FilledQuantity,
            fillPrice, fillPrice, intent.StopLossPrice, intent.TargetPrice, intent.PatternCode,
            intent.CustomPatternName, Utc(evidence.FilledAtUtc.Value), null, null, fillPrice,
            0m, Math.Abs(fillPrice - intent.StopLossPrice), false, false, false,
            null, null, null, false, null, null, null, [],
            new TradingPositionExecutionContext(
                intent.ExecutionArtifact, intent.MarketDataEvidence));
    }

    public static string PositionId(string commandId) => $"position:{commandId}";

    public static TradingRecommendationProjection MarkExecuted(
        TradingRecommendationProjection recommendation,
        Broker.BrokerOrderEvidence evidence) => recommendation with
        {
            WasExecuted = true,
            EntryOrderId = evidence.OrderId,
            EntryExecutionNote = null,
        };

    public static TradingRecommendationProjection MarkRejected(
        TradingRecommendationProjection recommendation,
        Broker.BrokerOrderEvidence evidence) => recommendation with
        {
            EntryRequestedAtUtc = null,
            EntryOrderId = evidence.OrderId,
            EntryExecutionNote = $"broker-{evidence.Status.ToLowerInvariant()}",
        };

    public static TradingRecommendationProjection MarkRejected(
        TradingRecommendationProjection recommendation,
        string reason) => recommendation with
        {
            EntryRequestedAtUtc = null,
            EntryExecutionNote = reason,
        };

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
