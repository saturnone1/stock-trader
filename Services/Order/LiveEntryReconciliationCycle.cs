using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Account;

namespace StockTrader.Services.Order;

/// <summary>
/// 미확정 진입 주문을 내구성 있게 저장된 소유 계좌별로 재조정합니다.
/// 한 계좌의 장애가 다른 계좌의 재조정을 막지 않습니다.
/// </summary>
public sealed class LiveEntryReconciliationCycle(
    ILiveEntryExecutionStore store,
    ILiveEntryExecutionCoordinator coordinator,
    IAccountManager accounts,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<LiveEntryReconciliationCycle> logger) : ILiveEntryReconciliationCycle
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var pending = await store.LoadPendingAsync(
            settings.Value.EntryReconciliationBatchSize,
            ct);
        if (pending.Count == 0)
            return;

        var invalid = pending
            .Where(item => !item.EntryAccountId.HasValue || !item.EntryRequestedAt.HasValue)
            .ToArray();
        foreach (var recommendation in invalid)
        {
            logger.LogCritical(
                "Cannot reconcile pending entry {RecommendationId}: durable account or request time is missing",
                recommendation.Id);
        }

        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var accountGroup in pending
                     .Where(item => item.EntryAccountId.HasValue && item.EntryRequestedAt.HasValue)
                     .GroupBy(item => item.EntryAccountId!.Value))
        {
            try
            {
                await ReconcileAccountAsync(accountGroup.Key, accountGroup.ToArray(), observedAt, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Pending entry reconciliation failed for account {AccountId}",
                    accountGroup.Key);
            }
        }
    }

    private async Task ReconcileAccountAsync(
        int accountId,
        IReadOnlyCollection<TradeRecommendation> recommendations,
        DateTime observedAt,
        CancellationToken ct)
    {
        var account = await accounts.GetBrokerContextForReconciliationAsync(accountId, ct);
        if (account is null)
        {
            logger.LogError(
                "Cannot reconcile {Count} pending entries: account {AccountId} is unavailable",
                recommendations.Count,
                accountId);
            return;
        }
        if (!BrokerCatalog.Get(account.Account.BrokerType).Capabilities.CanReadOrderHistory)
        {
            logger.LogCritical(
                "Cannot reconcile {Count} pending entries: broker {BrokerType} does not support order history",
                recommendations.Count,
                account.Account.BrokerType);
            return;
        }

        var requestedFrom = recommendations
            .Min(item => item.EntryRequestedAt!.Value)
            .AddSeconds(-2);
        var orders = await account.Broker.GetOrderHistoryAsync(
            requestedFrom,
            observedAt.AddSeconds(1),
            ct);

        foreach (var recommendation in recommendations)
        {
            try
            {
                var result = await coordinator.ReconcileAsync(
                    recommendation, account, orders, ct);
                if (result.Status is LiveEntryExecutionStatus.EvidenceMismatch
                    or LiveEntryExecutionStatus.AmbiguousEvidence)
                {
                    logger.LogCritical(
                        "Pending entry requires operator review: Recommendation={RecommendationId} "
                        + "Symbol={Symbol} Status={Status}",
                        recommendation.Id,
                        recommendation.Symbol,
                        result.Status);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Pending entry reconciliation failed: Recommendation={RecommendationId}",
                    recommendation.Id);
            }
        }
    }
}
