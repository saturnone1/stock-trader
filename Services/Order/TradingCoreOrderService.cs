using Microsoft.Extensions.Options;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Services.Notification;
using StockTrader.Services.TradingCore;

namespace StockTrader.Services.Order;

/// <summary>Routes financial entry authority to Trading Core after Remote cutover.</summary>
internal sealed class TradingCoreOrderService(
    ITradingCoreControlPlane core,
    ITradingAccountIdentitySource accounts,
    TradingCoreManualEntryPreparation manualEntries,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<TradingCoreOrderService> logger) : IOrderService
{
    public Task<bool> PlaceOrderAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default) => PlaceOrderAsync(recommendation, null, ct);

    public async Task<bool> PlaceOrderAsync(
        TradeRecommendation recommendation,
        int? accountId,
        CancellationToken ct = default)
    {
        if (recommendation.ExecutionArtifact is null || recommendation.MarketDataEvidence is null)
            throw new InvalidOperationException("missing-immutable-trading-execution-context");
        if (recommendation.SourceSignalId is null)
            throw new InvalidOperationException("missing-source-signal-identity");

        var status = await core.GetStatusAsync(ct);
        if (status.Mode != TradingAuthorityMode.Remote)
            throw new InvalidOperationException("trading-core-remote-authority-not-active");
        var now = clock.GetUtcNow().UtcDateTime;
        if (recommendation.Mode == OrderMode.AlertOnly)
        {
            var recommendationCommandId = "recommendation:" + CanonicalJsonHash.Compute(new
            {
                SourceSignalId = recommendation.SourceSignalId.Value,
                recommendation.ExecutionArtifact.ArtifactId,
                recommendation.MarketDataEvidence.EvidenceId,
            });
            var recommendationEnvelope = Envelope(
                recommendationCommandId, TradingCommandKinds.RecordRecommendation,
                recommendation.SourceSignalId.Value, status, now);
            var observation = new TradingRecommendationObservation(
                recommendationEnvelope,
                recommendation.SourceSignalId.Value.ToString(),
                recommendation.Symbol,
                recommendation.PatternType.ToString(),
                recommendation.CustomPatternName,
                recommendation.EntryPrice,
                recommendation.StopLossPrice,
                recommendation.TargetPrice,
                recommendation.ShareQuantity,
                recommendation.Expectancy,
                recommendation.ExecutionArtifact,
                recommendation.MarketDataEvidence);
            observation = observation with
            {
                Envelope = recommendationEnvelope with
                {
                    PayloadHash = TradingCoreIdentity.RecommendationPayload(observation)
                }
            };
            var recorded = await core.SubmitRecommendationAsync(observation, ct);
            if (recorded.Status == TradingCommandStatuses.Completed && !recorded.AlreadyAccepted)
                notifications.Notify(recommendation);
            return recorded.Status == TradingCommandStatuses.Completed;
        }
        var resolvedAccount = accountId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? await accounts.GetActiveAccountIdAsync(ct)
            ?? throw new InvalidOperationException("active-trading-account-missing");
        var intent = TradingCoreEntryIntentFactory.Create(
            recommendation, resolvedAccount, status, now);
        var receipt = await core.SubmitEntryAsync(intent, ct);
        logger.LogInformation(
            "Trading Core accepted entry {CommandId} for {Symbol}: {Status}",
            receipt.CommandId, recommendation.Symbol, receipt.Status);
        if (receipt.Status is not TradingCommandStatuses.Rejected && !receipt.AlreadyAccepted)
            notifications.Notify(recommendation);
        return receipt.Status is not TradingCommandStatuses.Rejected;
    }

    public async Task<(bool Success, string Message)> PlaceManualOrderAsync(
        long signalId,
        CancellationToken ct = default)
    {
        var prepared = await manualEntries.PrepareAsync(signalId, ct);
        if (!prepared.Succeeded)
            return (false, prepared.Message);
        var accepted = await PlaceOrderAsync(prepared.Recommendation!, ct);
        return accepted
            ? (true, $"{prepared.Recommendation!.Symbol} 원격 주문이 내구성 있게 접수됐습니다. 체결 상태를 확인하세요.")
            : (false, $"{prepared.Recommendation!.Symbol} 주문이 Trading Core 최종 위험 점검에서 거부됐습니다.");
    }

    private static TradingCommandEnvelope Envelope(
        string commandId,
        string kind,
        long sourceSignalId,
        TradingCoreStatus status,
        DateTime now) => new(
        TradingCoreContractVersions.Current,
        commandId,
        kind,
        string.Empty,
        commandId,
        sourceSignalId.ToString(),
        status.AuthorityGeneration,
        status.AccountGeneration,
        now,
        now.AddMinutes(5));
}
