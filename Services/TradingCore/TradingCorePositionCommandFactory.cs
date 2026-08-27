using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

internal static class TradingCorePositionCommandFactory
{
    public static TradingPositionCommand Create(
        TradingCoreStatus status,
        TradingPositionProjection position,
        string action,
        int quantity,
        string reason,
        MarketDataEvidenceContract evidence,
        DateTime occurredAtUtc,
        int? scalingRuleIndex = null,
        bool marksPartialProfit = false)
    {
        var artifact = position.ExecutionContext?.ExecutionArtifact
            ?? throw new InvalidOperationException("position-execution-context-missing");
        var commandIdentity = CanonicalJsonHash.Compute(new
        {
            status.AuthorityGeneration,
            position.PositionId,
            Action = action,
            Quantity = quantity,
            Reason = reason,
            artifact.ArtifactId,
            evidence.EvidenceId,
            ScalingRuleIndex = scalingRuleIndex,
            MarksPartialProfit = marksPartialProfit,
        });
        var commandId = "position:" + commandIdentity;
        var envelope = new TradingCommandEnvelope(
            TradingCoreContractVersions.Current,
            commandId,
            TradingCommandKinds.ClosePosition,
            string.Empty,
            commandId,
            position.SourceSignalId,
            status.AuthorityGeneration,
            status.AccountGeneration,
            occurredAtUtc,
            occurredAtUtc.AddMinutes(5));
        var command = new TradingPositionCommand(
            envelope,
            position.PositionId,
            action,
            quantity,
            reason,
            artifact.ArtifactId,
            evidence,
            scalingRuleIndex,
            marksPartialProfit);
        return command with
        {
            Envelope = envelope with { PayloadHash = TradingCoreIdentity.PositionPayload(command) }
        };
    }
}
