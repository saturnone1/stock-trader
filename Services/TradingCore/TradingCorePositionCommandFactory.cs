using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.MarketData;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Models;

namespace StockTrader.Services.TradingCore;

internal static class TradingCorePositionCommandFactory
{
    public static TradingPositionPolicyStateUpdate CreateStateUpdate(
        TradingCoreStatus status,
        TradingPositionProjection projection,
        Position evaluated,
        MarketDataEvidenceContract evidence,
        DateTime occurredAtUtc)
    {
        var artifact = projection.ExecutionContext?.ExecutionArtifact
            ?? throw new InvalidOperationException("position-execution-context-missing");
        var identity = CanonicalJsonHash.Compute(new
        {
            status.AuthorityGeneration,
            projection.PositionId,
            artifact.ArtifactId,
            evaluated.HighSinceEntry,
            evaluated.StopLossPrice,
            evaluated.InitialRiskDistance,
            evaluated.BreakevenApplied,
            evaluated.TrailingStopActivated,
            evidence.EvidenceId,
        });
        var commandId = "position-state:" + identity;
        var envelope = new TradingCommandEnvelope(
            TradingCoreContractVersions.Current,
            commandId,
            TradingCommandKinds.UpdatePositionState,
            string.Empty,
            commandId,
            projection.SourceSignalId,
            status.AuthorityGeneration,
            status.AccountGeneration,
            occurredAtUtc,
            occurredAtUtc.AddMinutes(5));
        var update = new TradingPositionPolicyStateUpdate(
            envelope,
            projection.PositionId,
            artifact.ArtifactId,
            evaluated.HighSinceEntry,
            evaluated.StopLossPrice,
            evaluated.InitialRiskDistance,
            evaluated.BreakevenApplied,
            evaluated.TrailingStopActivated,
            evidence);
        return update with
        {
            Envelope = envelope with
            {
                PayloadHash = TradingCoreIdentity.PositionStatePayload(update)
            }
        };
    }

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
