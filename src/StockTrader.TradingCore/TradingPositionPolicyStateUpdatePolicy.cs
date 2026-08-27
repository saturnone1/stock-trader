using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public static class TradingPositionPolicyStateUpdatePolicy
{
    public static TradingPositionProjection Apply(
        TradingPositionProjection position,
        TradingPositionPolicyStateUpdate update)
    {
        if (position.ClosedAtUtc.HasValue)
            throw new InvalidOperationException("position-already-closed");
        if (position.ExecutionRequestedAtUtc.HasValue)
            throw new InvalidOperationException("position-command-already-pending");
        if (position.ExecutionContext?.ExecutionArtifact.ArtifactId
            != update.ExpectedExecutionArtifactId)
            throw new InvalidOperationException("position-execution-artifact-mismatch");
        if (update.HighSinceEntry < position.HighSinceEntry
            || update.StopLossPrice < position.StopLossPrice
            || (position.InitialRiskDistance > 0
                && update.InitialRiskDistance != position.InitialRiskDistance)
            || (position.BreakevenApplied && !update.BreakevenApplied)
            || (position.TrailingStopActivated && !update.TrailingStopActivated))
            throw new InvalidOperationException("position-policy-state-regression");
        if (update.EntryAtr < 0)
            throw new InvalidOperationException("position-entry-atr-invalid");
        if (update.EvaluatedThroughBarUtc == default
            || (position.LastEvaluatedBarUtc.HasValue
                && update.EvaluatedThroughBarUtc <= position.LastEvaluatedBarUtc.Value))
            throw new InvalidOperationException("position-evaluated-bar-regression");
        return position with
        {
            HighSinceEntry = update.HighSinceEntry,
            StopLossPrice = update.StopLossPrice,
            InitialRiskDistance = update.InitialRiskDistance,
            BreakevenApplied = update.BreakevenApplied,
            TrailingStopActivated = update.TrailingStopActivated,
            EntryAtr = position.EntryAtr > 0 ? position.EntryAtr : update.EntryAtr,
            ExecutionContext = position.ExecutionContext,
            LastEvaluatedEvidenceId = update.MarketDataEvidence.EvidenceId,
            LastEvaluatedBarUtc = update.EvaluatedThroughBarUtc,
            LastEvaluatedMarketDataRevision = update.EvaluatedMarketDataRevision,
        };
    }
}
