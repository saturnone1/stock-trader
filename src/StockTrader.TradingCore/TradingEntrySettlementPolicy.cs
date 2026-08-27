using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public static class TradingEntrySettlementPolicy
{
    public static TradingPositionProjection CreateTerminalPosition(
        TradingEntryIntent intent,
        Broker.BrokerOrderEvidence evidence,
        DateTime observedAtUtc)
    {
        if (!IsCompatibleTerminalFill(evidence.Status, evidence.FilledQuantity, intent.ShareQuantity)
            || evidence.AverageFillPrice is not > 0m
            || observedAtUtc == default)
            throw new ArgumentException("Broker evidence is not a compatible terminal entry fill.", nameof(evidence));
        var fillPrice = evidence.AverageFillPrice.Value;
        var filledAt = evidence.FilledAtUtc ?? observedAtUtc;
        return new TradingPositionProjection(
            PositionId(intent.Envelope.CommandId), intent.SourceSignalId, intent.AccountId,
            intent.Symbol, intent.Sector, evidence.FilledQuantity, evidence.FilledQuantity,
            fillPrice, fillPrice, intent.StopLossPrice, intent.TargetPrice, intent.PatternCode,
            intent.CustomPatternName, Utc(filledAt), null, null, fillPrice,
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
            EntryExecutionNote = string.Equals(evidence.Status, "Filled", StringComparison.Ordinal)
                ? null
                : $"broker-{evidence.Status.ToLowerInvariant()}-after-partial-fill",
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

    private static bool IsCompatibleTerminalFill(string status, int filled, int requested) =>
        string.Equals(status, "Filled", StringComparison.Ordinal)
            ? filled == requested
            : status is "Rejected" or "Cancelled" or "Expired"
                && filled > 0 && filled <= requested;
}
