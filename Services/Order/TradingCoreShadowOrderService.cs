using StockTrader.Application.MarketData;
using StockTrader.Application.TradingCore;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.Services.TradingCore;

namespace StockTrader.Services.Order;

/// <summary>
/// Keeps Local as the only writer and broker caller, then sends immutable decision evidence to the
/// broker-isolated candidate. Shadow transport failure never changes the authoritative result.
/// </summary>
internal sealed class TradingCoreShadowOrderService(
    OrderService local,
    ITradingCoreControlPlane core,
    ITradingAccountIdentitySource accounts,
    TradingCoreManualEntryPreparation manualEntries,
    IMarketCalendar calendar,
    TimeProvider clock,
    ILogger<TradingCoreShadowOrderService> logger) : IOrderService
{
    public Task<bool> PlaceOrderAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default) => PlaceOrderAsync(recommendation, null, ct);

    public async Task<bool> PlaceOrderAsync(
        TradeRecommendation recommendation,
        int? accountId,
        CancellationToken ct = default)
    {
        var observedAt = clock.GetUtcNow().UtcDateTime;
        var marketOpen = calendar.IsMarketOpen(MarketRegion.UnitedStates);
        var authoritative = await local.PlaceOrderAsync(recommendation, accountId, ct);
        await CompareAsync(
            recommendation, accountId, authoritative, observedAt, marketOpen, ct);
        return authoritative;
    }

    public async Task<(bool Success, string Message)> PlaceManualOrderAsync(
        long signalId,
        CancellationToken ct = default)
    {
        var observedAt = clock.GetUtcNow().UtcDateTime;
        var marketOpen = calendar.IsMarketOpen(MarketRegion.UnitedStates);
        var candidate = await manualEntries.PrepareAsync(signalId, ct);
        var authoritative = await local.PlaceManualOrderAsync(signalId, ct);
        if (candidate.Succeeded)
            await CompareAsync(
                candidate.Recommendation!, null, authoritative.Success,
                observedAt, marketOpen, ct);
        else
            logger.LogWarning(
                "Trading Core manual Shadow input unavailable for signal {SignalId}: {Reason}",
                signalId, candidate.Message);
        return authoritative;
    }

    private async Task CompareAsync(
        TradeRecommendation recommendation,
        int? accountId,
        bool authoritative,
        DateTime observedAt,
        bool marketOpen,
        CancellationToken ct)
    {
        try
        {
            var status = await core.GetStatusAsync(ct);
            if (status.Mode != TradingAuthorityMode.Shadow)
                throw new InvalidOperationException("trading-core-shadow-authority-not-active");
            var resolvedAccount = accountId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                ?? await accounts.GetActiveAccountIdAsync(ct)
                ?? throw new InvalidOperationException("active-trading-account-missing");
            var intent = TradingCoreEntryIntentFactory.Create(
                recommendation, resolvedAccount, status, observedAt);
            var disposition = recommendation.Mode == OrderMode.AlertOnly
                ? TradingShadowDispositions.RecommendationOnly
                : marketOpen && authoritative
                    ? TradingShadowDispositions.BrokerSubmission
                    : TradingShadowDispositions.Blocked;
            var reason = disposition == TradingShadowDispositions.Blocked
                ? marketOpen ? "local-order-rejected" : "market-closed"
                : null;
            var observation = new TradingShadowEntryObservation(
                TradingCoreContractVersions.Current, string.Empty, string.Empty, observedAt,
                recommendation.Mode.ToString(), disposition, reason, intent);
            var payloadHash = TradingCoreIdentity.ShadowEntryPayload(observation);
            observation = observation with
            {
                DecisionId = $"shadow:{payloadHash}",
                PayloadHash = payloadHash,
            };
            var receipt = await core.CompareShadowEntryAsync(observation, ct);
            if (!receipt.IsMatch)
                logger.LogError(
                    "Trading Core Shadow mismatch {DecisionId}: Local={Local} Candidate={Candidate}/{Reason}",
                    receipt.DecisionId, receipt.AuthoritativeDisposition,
                    receipt.CandidateDisposition, receipt.CandidateReason);
            else
                logger.LogInformation(
                    "Trading Core Shadow parity confirmed for {DecisionId}: {Disposition}",
                    receipt.DecisionId, receipt.CandidateDisposition);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error,
                "Trading Core Shadow comparison failed; Local financial result remains authoritative");
        }
    }
}
