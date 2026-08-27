using StockTrader.Models;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

internal static class TradingCoreEntryIntentFactory
{
    public static TradingEntryIntent Create(
        TradeRecommendation recommendation,
        string accountId,
        TradingCoreStatus status,
        DateTime observedAtUtc)
    {
        if (recommendation.ExecutionArtifact is null || recommendation.MarketDataEvidence is null)
            throw new InvalidOperationException("missing-immutable-trading-execution-context");
        if (recommendation.SourceSignalId is null)
            throw new InvalidOperationException("missing-source-signal-identity");
        var commandId = "entry:" + CanonicalJsonHash.Compute(new
        {
            SourceSignalId = recommendation.SourceSignalId.Value,
            AccountId = accountId,
            recommendation.ExecutionArtifact.ArtifactId,
            recommendation.MarketDataEvidence.EvidenceId,
        });
        var envelope = new TradingCommandEnvelope(
            TradingCoreContractVersions.Current,
            commandId,
            TradingCommandKinds.AcceptEntry,
            string.Empty,
            commandId,
            recommendation.SourceSignalId.Value.ToString(),
            status.AuthorityGeneration,
            status.AccountGeneration,
            observedAtUtc,
            observedAtUtc.AddMinutes(5));
        var intent = new TradingEntryIntent(
            envelope,
            recommendation.SourceSignalId.Value.ToString(),
            accountId,
            recommendation.Symbol,
            recommendation.ExecutionSector,
            recommendation.PatternType.ToString(),
            recommendation.CustomPatternName,
            recommendation.EntryPrice,
            recommendation.StopLossPrice,
            recommendation.TargetPrice,
            recommendation.ShareQuantity,
            recommendation.Expectancy,
            recommendation.ExecutionArtifact,
            recommendation.MarketDataEvidence);
        return intent with
        {
            Envelope = envelope with { PayloadHash = TradingCoreIdentity.EntryPayload(intent) }
        };
    }
}
