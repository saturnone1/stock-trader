using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.Settings;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Notification;

namespace StockTrader.Services.Order;

/// <summary>
/// 오픈 포지션을 내구성 있게 저장된 소유 계좌별로 재조정·평가합니다.
/// 계좌 미지정 레거시 포지션만 현재 활성 계좌를 명시적 호환 경로로 사용합니다.
/// </summary>
public sealed class LivePositionMonitoringCycle(
    IOpenPositionStore positions,
    IOhlcvRepository ohlcvRepository,
    ILiveParameterService liveParameters,
    ICompiledStrategyRepository strategies,
    IAccountManager accounts,
    ILivePositionExecutionCoordinator executionCoordinator,
    ILivePositionExecutionEvaluator executionEvaluator,
    INotificationService notifications,
    IOptions<TradingSettings> settings,
    IFinancialCycleBarrier financialBarrier,
    TimeProvider timeProvider,
    ILogger<LivePositionMonitoringCycle> logger) : ILivePositionMonitoringCycle
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var cycleLease = await financialBarrier.TryEnterPositionCycleAsync(ct);
        if (cycleLease is null)
            return;
        var openPositions = await positions.GetOpenPositionsAsync(ct);
        if (openPositions.Count == 0)
            return;

        var pending = openPositions
            .Where(position => position.ExecutionRequestedAt.HasValue)
            .ToArray();
        var evaluable = openPositions
            .Where(position => !position.ExecutionRequestedAt.HasValue)
            .ToArray();
        await ReconcilePendingAsync(pending, ct);

        if (evaluable.Length == 0)
            return;

        var liveOverrides = (await liveParameters.GetAsync(ct)).Overrides;
        var customStrategies = await strategies.GetByNamesAsync(
            evaluable.Select(position => position.CustomPatternName).OfType<string>(), ct);

        foreach (var accountGroup in evaluable.GroupBy(position => position.AccountId))
        {
            await EvaluateAccountPositionsAsync(
                accountGroup.Key,
                accountGroup.ToArray(),
                customStrategies,
                liveOverrides,
                ct);
        }
    }

    private async Task ReconcilePendingAsync(
        IReadOnlyCollection<Position> pending,
        CancellationToken ct)
    {
        foreach (var accountGroup in pending.GroupBy(position => position.AccountId))
        {
            var account = await ResolveAccountAsync(
                accountGroup.Key,
                reconciliationOnly: true,
                ct);
            if (account is null)
            {
                logger.LogCritical(
                    "Cannot reconcile {Count} pending position orders: owning account {AccountId} is unavailable",
                    accountGroup.Count(),
                    accountGroup.Key);
                continue;
            }

            foreach (var position in accountGroup)
            {
                var reconciliation = await executionCoordinator.ReconcileAsync(
                    position, account.Broker, ct: ct);
                HandleExecutionReconciliation(position, reconciliation);
            }
        }
    }

    private async Task EvaluateAccountPositionsAsync(
        int accountId,
        IReadOnlyCollection<Position> accountPositions,
        IReadOnlyDictionary<string, CompiledStrategy> customStrategies,
        PatternParameterOverrides? liveOverrides,
        CancellationToken ct)
    {
        var account = await ResolveAccountAsync(accountId, reconciliationOnly: false, ct);
        if (account is null)
        {
            logger.LogError(
                "Cannot monitor {Count} positions: owning account {AccountId} is unavailable",
                accountPositions.Count,
                accountId);
            return;
        }
        if (!BrokerCatalog.CanMonitorPositions(account.Broker.BrokerType))
        {
            logger.LogError(
                "Cannot monitor account {AccountId}: broker {BrokerType} lacks account or position reads",
                account.Account.Id,
                account.Broker.BrokerType);
            return;
        }

        var brokerAccount = await account.Broker.GetAccountAsync(ct);
        if (brokerAccount is null)
        {
            logger.LogError(
                "Cannot monitor account {AccountId}: account snapshot is unavailable",
                account.Account.Id);
            return;
        }
        var brokerPositions = await account.Broker.GetPositionsAsync(ct);
        var brokerPriceMap = brokerPositions.ToDictionary(
            position => position.Symbol,
            position => position.CurrentPrice,
            StringComparer.OrdinalIgnoreCase);

        foreach (var position in accountPositions)
        {
            try
            {
                await EvaluatePositionAsync(
                    position,
                    account,
                    brokerPriceMap,
                    brokerAccount.TotalEquity,
                    customStrategies,
                    liveOverrides,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Error evaluating position order for {Symbol} in account {AccountId}",
                    position.Symbol,
                    account.Account.Id);
            }
        }
    }

    private async Task EvaluatePositionAsync(
        Position position,
        AccountBrokerContext account,
        IReadOnlyDictionary<string, decimal> brokerPriceMap,
        decimal currentEquity,
        IReadOnlyDictionary<string, CompiledStrategy> customStrategies,
        PatternParameterOverrides? liveOverrides,
        CancellationToken ct)
    {
        if (brokerPriceMap.TryGetValue(position.Symbol, out var currentPrice)
            && currentPrice > 0)
        {
            position.CurrentPrice = currentPrice;
        }
        if (position.CurrentPrice <= 0)
            return;

        var before = PositionPolicyState.Capture(position);
        customStrategies.TryGetValue(
            position.CustomPatternName ?? string.Empty,
            out var customStrategy);
        var decision = await executionEvaluator.EvaluateAsync(
            position,
            customStrategy,
            ohlcvRepository,
            liveOverrides,
            ct,
            currentEquity,
            settings.Value.MaxTotalPositions);

        if (!decision.ShouldExecute)
        {
            if (before.HasChanged(position))
                await positions.SavePositionAsync(position, ct);
            return;
        }

        logger.LogInformation(
            "[POSITION-ORDER] {Symbol} Account={AccountId} — {Reason} "
            + "(Entry={Entry:F2}, Current={Current:F2}, PnL={PnL:P2})",
            position.Symbol,
            position.AccountId,
            decision.Reason,
            position.EntryPrice,
            position.CurrentPrice,
            position.CurrentPrice / position.EntryPrice - 1);
        var intent = decision.Intent!;
        if (intent.Kind == PositionExecutionKind.ScaleIn
            && !account.Account.IsEnabled)
        {
            logger.LogWarning(
                "[POSITION-ORDER] {Symbol} Account={AccountId}: disabled owning account blocks scale-in",
                position.Symbol,
                account.Account.Id);
            return;
        }
        var submission = await executionCoordinator.SubmitAsync(
            position,
            new LivePositionExecutionRequest(
                intent.Quantity,
                intent.Reason,
                intent.Kind,
                intent.ScalingRuleIndex,
                intent.MarksPartialProfit),
            account.Broker,
            ct);
        if (submission.Status != LivePositionExecutionSubmissionStatus.Accepted
            || submission.Order is null
            || !submission.RequestedAt.HasValue)
            return;

        var reconciliation = await executionCoordinator.ReconcileAsync(
            position, account.Broker, [submission.Order], ct);
        if (reconciliation.Status == LivePositionExecutionReconciliationStatus.AwaitingBroker)
        {
            reconciliation = await WaitForExecutionResolutionAsync(
                position, account.Broker, ct);
        }
        HandleExecutionReconciliation(position, reconciliation);
    }

    private async Task<AccountBrokerContext?> ResolveAccountAsync(
        int accountId,
        bool reconciliationOnly,
        CancellationToken ct) => accountId > 0
        ? reconciliationOnly
            ? await accounts.GetBrokerContextForReconciliationAsync(accountId, ct)
            : await accounts.GetBrokerContextForPositionExitAsync(accountId, ct)
        : await accounts.GetBrokerContextAsync(null, ct);

    private async Task<LivePositionExecutionReconciliationResult>
        WaitForExecutionResolutionAsync(
            Position position,
            IBrokerService broker,
            CancellationToken ct)
    {
        for (var attempt = 0;
             attempt < settings.Value.PositionOrderResolutionMaxAttempts;
             attempt++)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    settings.Value.PositionOrderResolutionDelayMilliseconds),
                timeProvider,
                ct);
            var reconciliation = await executionCoordinator.ReconcileAsync(
                position, broker, ct: ct);
            if (reconciliation.Status
                != LivePositionExecutionReconciliationStatus.AwaitingBroker)
                return reconciliation;
        }
        return new LivePositionExecutionReconciliationResult(
            LivePositionExecutionReconciliationStatus.AwaitingBroker);
    }

    private void HandleExecutionReconciliation(
        Position position,
        LivePositionExecutionReconciliationResult reconciliation)
    {
        if (reconciliation.Status == LivePositionExecutionReconciliationStatus.ReleasedForRetry)
        {
            logger.LogWarning(
                "[POSITION-ORDER] {Symbol}: 주문 {OrderId}가 {Status} 상태여서 재평가를 허용합니다.",
                position.Symbol,
                reconciliation.Order?.OrderId,
                reconciliation.Order?.Status);
            return;
        }
        if (reconciliation.Status
            == LivePositionExecutionReconciliationStatus.BrokerFillMismatch)
        {
            logger.LogError(
                "[POSITION-ORDER] {Symbol}: 요청 수량 {RequestedQuantity}주와 브로커 체결 수량 "
                + "{FilledQuantity}주가 다릅니다. 자동 반영을 중단합니다.",
                position.Symbol,
                position.ExecutionRequestQuantity ?? position.Quantity,
                reconciliation.FilledQuantity);
            return;
        }
        if (reconciliation.Status != LivePositionExecutionReconciliationStatus.Completed)
        {
            logger.LogDebug(
                "[POSITION-ORDER] {Symbol}: 주문 {OrderId}의 확정 상태를 기다립니다.",
                position.Symbol,
                position.ExecutionOrderId);
            return;
        }

        notifications.Notify(new TradeRecommendation
        {
            Symbol = position.Symbol,
            PatternType = position.PatternType,
            CustomPatternName = position.CustomPatternName,
            EntryPrice = position.EntryPrice,
            TargetPrice = position.ExitPrice ?? position.CurrentPrice,
            ShareQuantity = reconciliation.FilledQuantity,
            GeneratedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    private sealed record PositionPolicyState(
        decimal HighSinceEntry,
        decimal StopLossPrice,
        decimal InitialRiskDistance,
        bool BreakevenApplied,
        bool TrailingStopActivated)
    {
        public static PositionPolicyState Capture(Position position) => new(
            position.HighSinceEntry,
            position.StopLossPrice,
            position.InitialRiskDistance,
            position.BreakevenApplied,
            position.TrailingStopActivated);

        public bool HasChanged(Position position) =>
            position.HighSinceEntry != HighSinceEntry
            || position.StopLossPrice != StopLossPrice
            || position.InitialRiskDistance != InitialRiskDistance
            || position.BreakevenApplied != BreakevenApplied
            || position.TrailingStopActivated != TrailingStopActivated;
    }
}
