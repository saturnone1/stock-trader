using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Services.Account;
using StockTrader.Services.Order;

namespace StockTrader.BackgroundServices;

/// <summary>재시작 뒤에도 미확정 신규 진입을 브로커 주문 내역과 재조정한다.</summary>
public sealed class EntryExecutionReconciliationService(
    IServiceScopeFactory scopeFactory,
    IAccountManager accounts,
    IOptions<TradingSettings> settings,
    TimeProvider timeProvider,
    ILogger<EntryExecutionReconciliationService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("EntryExecutionReconciliationService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcilePendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Pending entry reconciliation cycle failed");
            }

            var seconds = Math.Clamp(
                settings.Value.EntryReconciliationIntervalSeconds, 5, 300);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
        logger.LogInformation("EntryExecutionReconciliationService stopped");
    }

    private async Task ReconcilePendingAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ILiveEntryExecutionStore>();
        var coordinator = scope.ServiceProvider
            .GetRequiredService<ILiveEntryExecutionCoordinator>();
        var pending = await store.LoadPendingAsync(
            settings.Value.EntryReconciliationBatchSize,
            ct);

        foreach (var accountGroup in pending
                     .Where(item => item.EntryAccountId.HasValue)
                     .GroupBy(item => item.EntryAccountId!.Value))
        {
            var account = await accounts.GetBrokerContextForReconciliationAsync(
                accountGroup.Key, ct);
            if (account is null)
            {
                logger.LogError(
                    "Cannot reconcile {Count} pending entries: account {AccountId} is unavailable",
                    accountGroup.Count(),
                    accountGroup.Key);
                continue;
            }

            var requestedFrom = accountGroup
                .Min(item => item.EntryRequestedAt!.Value)
                .AddSeconds(-2);
            var orders = await account.Broker.GetOrderHistoryAsync(
                requestedFrom,
                timeProvider.GetUtcNow().UtcDateTime.AddSeconds(1),
                ct);
            foreach (var recommendation in accountGroup)
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
                catch (Exception exception)
                {
                    logger.LogError(exception,
                        "Pending entry reconciliation failed: Recommendation={RecommendationId}",
                        recommendation.Id);
                }
            }
        }
    }
}
