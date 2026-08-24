using Microsoft.Extensions.Options;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Data.Repositories;

namespace StockTrader.Services.Order;

/// <summary>Routes financial entry authority to Trading Core after Remote cutover.</summary>
internal sealed class TradingCoreOrderService(
    ITradingCoreControlPlane core,
    ITradingAccountIdentitySource accounts,
    ISettingsRepository settings,
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
        var userSettings = await settings.GetAsync(ct);
        if (userSettings.OrderMode == OrderMode.AlertOnly)
            throw new InvalidOperationException(
                "remote-alert-only-routing-must-use-non-financial-recommendation-path");
        if (recommendation.ExecutionArtifact is null || recommendation.MarketDataEvidence is null)
            throw new InvalidOperationException("missing-immutable-trading-execution-context");
        if (recommendation.SourceSignalId is null)
            throw new InvalidOperationException("missing-source-signal-identity");

        var status = await core.GetStatusAsync(ct);
        if (status.Mode != TradingAuthorityMode.Remote)
            throw new InvalidOperationException("trading-core-remote-authority-not-active");
        var resolvedAccount = accountId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
            ?? await accounts.GetActiveAccountIdAsync(ct)
            ?? throw new InvalidOperationException("active-trading-account-missing");
        var now = clock.GetUtcNow().UtcDateTime;
        var commandId = "entry:" + CanonicalJsonHash.Compute(new
        {
            SourceSignalId = recommendation.SourceSignalId.Value,
            AccountId = resolvedAccount,
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
            now,
            now.AddMinutes(5));
        var intent = new TradingEntryIntent(
            envelope,
            recommendation.SourceSignalId.Value.ToString(),
            resolvedAccount,
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
        intent = intent with
        {
            Envelope = envelope with { PayloadHash = TradingCoreIdentity.EntryPayload(intent) }
        };
        var receipt = await core.SubmitEntryAsync(intent, ct);
        logger.LogInformation(
            "Trading Core accepted entry {CommandId} for {Symbol}: {Status}",
            receipt.CommandId, recommendation.Symbol, receipt.Status);
        return receipt.Status is not TradingCommandStatuses.Rejected;
    }

    public Task<(bool Success, string Message)> PlaceManualOrderAsync(
        long signalId,
        CancellationToken ct = default) => Task.FromResult((false,
            "Trading Core 전환 후 수동 주문은 불변 실행 근거 생성 경로가 준비될 때까지 차단됩니다."));
}
