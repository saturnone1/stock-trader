using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Order;

public enum LivePositionExecutionSubmissionStatus
{
    Accepted,
    AlreadyPending,
    Failed,
    Unsupported,
}

public sealed record LivePositionExecutionSubmission(
    LivePositionExecutionSubmissionStatus Status,
    DateTime? RequestedAt = null,
    BrokerOrder? Order = null,
    bool BrokerOrderIdPersisted = false);

public sealed record LivePositionExecutionRequest(
    int Quantity,
    string Reason,
    PositionExecutionKind Kind = PositionExecutionKind.FullExit,
    int? ScalingRuleIndex = null,
    bool MarksPartialProfit = false);

public enum LivePositionExecutionReconciliationStatus
{
    NotPending,
    AwaitingBroker,
    ReleasedForRetry,
    Completed,
    ConcurrentChange,
    BrokerFillMismatch,
    Unsupported,
}

public sealed record LivePositionExecutionReconciliationResult(
    LivePositionExecutionReconciliationStatus Status,
    BrokerOrder? Order = null,
    int FilledQuantity = 0,
    bool IsFullExit = false);

public interface ILivePositionExecutionCoordinator
{
    Task<LivePositionExecutionSubmission> SubmitFullExitAsync(
        Position position,
        string reason,
        IBrokerService broker,
        CancellationToken ct = default);

    Task<LivePositionExecutionSubmission> SubmitAsync(
        Position position,
        LivePositionExecutionRequest request,
        IBrokerService broker,
        CancellationToken ct = default);

    Task<LivePositionExecutionReconciliationResult> ReconcileAsync(
        Position position,
        IBrokerService broker,
        IReadOnlyCollection<BrokerOrder>? knownOrders = null,
        CancellationToken ct = default);
}

/// <summary>
/// 자동·수동 청산과 스케일링 주문을 동일한 DB 선점·브로커 증거·원자 체결 절차로 처리한다.
/// </summary>
public sealed class LivePositionExecutionCoordinator : ILivePositionExecutionCoordinator
{
    private readonly ILivePositionExecutionStore _store;
    private readonly TimeProvider _timeProvider;

    public LivePositionExecutionCoordinator(
        ILivePositionExecutionStore store,
        TimeProvider timeProvider)
    {
        _store = store;
        _timeProvider = timeProvider;
    }

    public Task<LivePositionExecutionSubmission> SubmitFullExitAsync(
        Position position,
        string reason,
        IBrokerService broker,
        CancellationToken ct = default) =>
        SubmitAsync(
            position,
            new LivePositionExecutionRequest(position.Quantity, reason),
            broker,
            ct);

    public async Task<LivePositionExecutionSubmission> SubmitAsync(
        Position position,
        LivePositionExecutionRequest request,
        IBrokerService broker,
        CancellationToken ct = default)
    {
        if (position.ExecutionRequestedAt.HasValue)
        {
            return new LivePositionExecutionSubmission(
                LivePositionExecutionSubmissionStatus.AlreadyPending,
                position.ExecutionRequestedAt);
        }
        if (!IsValid(position, request))
            return new LivePositionExecutionSubmission(LivePositionExecutionSubmissionStatus.Failed);
        if (!SupportsSubmission(broker.BrokerType, request.Kind))
            return new LivePositionExecutionSubmission(LivePositionExecutionSubmissionStatus.Unsupported);

        var requestedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var claim = new PositionExecutionClaim(
            position.Id,
            requestedAt,
            request.Reason,
            position.Quantity,
            request.Quantity,
            request.Kind,
            request.ScalingRuleIndex,
            request.MarksPartialProfit);
        if (!await _store.TryClaimAsync(claim, ct))
        {
            return new LivePositionExecutionSubmission(
                LivePositionExecutionSubmissionStatus.AlreadyPending);
        }

        ApplyIntent(position, request, requestedAt);
        var order = await SubmitBrokerOrderAsync(position, request, broker, ct);
        if (order is null || !IsSubmittedOrderConsistent(position, request, order))
        {
            await _store.ReleaseClaimAsync(position.Id, requestedAt, ct);
            ClearIntent(position);
            return new LivePositionExecutionSubmission(LivePositionExecutionSubmissionStatus.Failed);
        }

        position.ExecutionOrderId = string.IsNullOrWhiteSpace(order.OrderId) ? null : order.OrderId;
        var orderIdPersisted = position.ExecutionOrderId is not null
            && await _store.SetOrderEvidenceAsync(
                position.Id, requestedAt, position.ExecutionOrderId, ct);
        if (position.ExecutionOrderId is not null && !orderIdPersisted)
            position.ExecutionOrderId = null;
        return new LivePositionExecutionSubmission(
            LivePositionExecutionSubmissionStatus.Accepted,
            requestedAt,
            order,
            orderIdPersisted);
    }

    public async Task<LivePositionExecutionReconciliationResult> ReconcileAsync(
        Position position,
        IBrokerService broker,
        IReadOnlyCollection<BrokerOrder>? knownOrders = null,
        CancellationToken ct = default)
    {
        if (!position.ExecutionRequestedAt.HasValue)
        {
            return new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.NotPending);
        }

        var requestedAt = position.ExecutionRequestedAt.Value;
        if (knownOrders is null
            && !BrokerCatalog.Get(broker.BrokerType).Capabilities.CanReadOrderHistory)
        {
            return new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.Unsupported);
        }
        var kind = position.ExecutionRequestKind ?? PositionExecutionKind.FullExit;
        var orders = knownOrders ?? await broker.GetOrderHistoryAsync(
            requestedAt.AddSeconds(-2),
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(1),
            ct);
        var resolution = PositionOrderReconciliationPolicy.Resolve(
            position.Symbol,
            position.ExecutionOrderId,
            requestedAt,
            kind == PositionExecutionKind.ScaleIn ? TradeDirection.Long : TradeDirection.Short,
            orders);

        if (resolution.Action == PositionOrderReconciliationAction.Wait)
        {
            return new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.AwaitingBroker, resolution.Order);
        }

        if (resolution.Action == PositionOrderReconciliationAction.ReleaseForRetry)
        {
            var released = await _store.ReleaseClaimAsync(
                position.Id, requestedAt, ct);
            if (!released)
            {
                return new LivePositionExecutionReconciliationResult(
                    LivePositionExecutionReconciliationStatus.ConcurrentChange, resolution.Order);
            }

            ClearIntent(position);
            return new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.ReleasedForRetry, resolution.Order);
        }

        if (resolution.Order?.AverageFillPrice is not > 0)
        {
            return new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.AwaitingBroker, resolution.Order);
        }

        var requestedQuantity = position.ExecutionRequestQuantity ?? position.Quantity;
        var filledQuantity = resolution.Order.FilledQuantity > 0
            ? resolution.Order.FilledQuantity
            : resolution.Order.Quantity > 0
                ? resolution.Order.Quantity
                : requestedQuantity;
        if (requestedQuantity <= 0 || filledQuantity != requestedQuantity)
        {
            return new LivePositionExecutionReconciliationResult(
                LivePositionExecutionReconciliationStatus.BrokerFillMismatch,
                resolution.Order,
                filledQuantity);
        }

        var fillPrice = resolution.Order.AverageFillPrice.Value;
        var fillTime = resolution.Order.FilledAt ?? _timeProvider.GetUtcNow().UtcDateTime;
        var fill = new PositionExecutionFill(
            position.Id,
            requestedAt,
            position.Quantity,
            filledQuantity,
            fillPrice,
            fillTime,
            position.ExecutionOrderId,
            kind,
            position.ExecutionRequestRuleIndex,
            position.ExecutionRequestMarksPartialProfit);
        var trade = kind == PositionExecutionKind.ScaleIn
            ? null
            : CreateExitTrade(position, filledQuantity, fillPrice, fillTime);
        var completed = await _store.CommitFillAsync(fill, trade, ct);
        if (completed)
            ApplyFill(position, fill);
        return new LivePositionExecutionReconciliationResult(
            completed
                ? LivePositionExecutionReconciliationStatus.Completed
                : LivePositionExecutionReconciliationStatus.ConcurrentChange,
            resolution.Order,
            completed ? filledQuantity : 0,
            completed && kind == PositionExecutionKind.FullExit);
    }

    private static bool IsValid(Position position, LivePositionExecutionRequest request) =>
        request.Quantity > 0
        && !string.IsNullOrWhiteSpace(request.Reason)
        && Enum.IsDefined(request.Kind)
        && (request.Kind != PositionExecutionKind.FullExit
            || request.Quantity == position.Quantity)
        && (request.Kind is not (PositionExecutionKind.PartialProfit
                or PositionExecutionKind.ScaleOut)
            || request.Quantity < position.Quantity)
        && (request.Kind is PositionExecutionKind.ScaleIn or PositionExecutionKind.ScaleOut)
            == (request.ScalingRuleIndex is >= 0)
        && (!request.MarksPartialProfit
            || request.Kind == PositionExecutionKind.PartialProfit);

    private static Task<BrokerOrder?> SubmitBrokerOrderAsync(
        Position position,
        LivePositionExecutionRequest request,
        IBrokerService broker,
        CancellationToken ct) => request.Kind switch
        {
            PositionExecutionKind.ScaleIn => broker.IncreasePositionAsync(
                position.Symbol, request.Quantity, ct),
            PositionExecutionKind.FullExit => broker.ClosePositionAsync(position.Symbol, ct),
            _ => broker.ClosePositionAsync(position.Symbol, request.Quantity, ct),
        };

    private static bool SupportsSubmission(BrokerType brokerType, PositionExecutionKind kind)
    {
        var capabilities = BrokerCatalog.Get(brokerType).Capabilities;
        return kind switch
        {
            PositionExecutionKind.ScaleIn => capabilities.CanScaleIn,
            PositionExecutionKind.FullExit => capabilities.CanCloseFullPosition,
            _ => capabilities.CanClosePartialPosition,
        };
    }

    private static bool IsSubmittedOrderConsistent(
        Position position,
        LivePositionExecutionRequest request,
        BrokerOrder order) =>
        order.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase)
        && order.Direction == (request.Kind == PositionExecutionKind.ScaleIn
            ? TradeDirection.Long
            : TradeDirection.Short)
        && order.Quantity == request.Quantity;

    private static PositionExecutionTrade CreateExitTrade(
        Position position,
        int quantity,
        decimal exitPrice,
        DateTime exitTime) => new(
            position.Symbol,
            position.PatternType,
            position.CustomPatternName,
            position.EntryPrice,
            exitPrice,
            quantity,
            position.OpenedAt,
            exitTime,
            (exitPrice - position.EntryPrice) * quantity,
            position.EntryPrice > 0 ? exitPrice / position.EntryPrice - 1 : 0,
            position.ExecutionRequestReason ?? "실시간 포지션 실행");

    private static void ApplyIntent(
        Position position,
        LivePositionExecutionRequest request,
        DateTime requestedAt)
    {
        position.ExecutionRequestedAt = requestedAt;
        position.ExecutionRequestReason = request.Reason;
        position.ExecutionRequestQuantity = request.Quantity;
        position.ExecutionRequestMarksPartialProfit = request.MarksPartialProfit;
        position.ExecutionRequestKind = request.Kind;
        position.ExecutionRequestRuleIndex = request.ScalingRuleIndex;
    }

    private static void ApplyFill(Position position, PositionExecutionFill fill)
    {
        if (fill.Kind == PositionExecutionKind.FullExit)
        {
            position.ClosedAt = fill.FilledAt;
            position.ExitPrice = fill.FillPrice;
            return;
        }

        if (fill.Kind == PositionExecutionKind.ScaleIn)
        {
            var totalCost = position.EntryPrice * position.Quantity
                + fill.FillPrice * fill.FilledQuantity;
            position.Quantity += fill.FilledQuantity;
            position.EntryPrice = totalCost / position.Quantity;
        }
        else
        {
            position.Quantity -= fill.FilledQuantity;
            position.PartialProfitTaken |= fill.MarksPartialProfit;
            if (fill.MarksPartialProfit)
            {
                position.StopLossPrice = Math.Max(position.StopLossPrice, position.EntryPrice);
                position.BreakevenApplied = true;
            }
        }

        position.CurrentPrice = fill.FillPrice;
        if (fill.Kind is PositionExecutionKind.ScaleIn or PositionExecutionKind.ScaleOut)
            RegisterScalingExecution(position, fill.ScalingRuleIndex!.Value);
        ClearIntent(position);
    }

    private static void RegisterScalingExecution(Position position, int ruleIndex)
    {
        var counter = position.ScalingExecutions.SingleOrDefault(item => item.RuleIndex == ruleIndex);
        if (counter is null)
        {
            position.ScalingExecutions.Add(new PositionScalingExecution
            {
                PositionId = position.Id,
                RuleIndex = ruleIndex,
                ExecutionCount = 1,
                Position = position,
            });
        }
        else
        {
            counter.ExecutionCount++;
        }
    }

    private static void ClearIntent(Position position)
    {
        position.ExecutionRequestedAt = null;
        position.ExecutionRequestReason = null;
        position.ExecutionRequestQuantity = null;
        position.ExecutionRequestMarksPartialProfit = false;
        position.ExecutionRequestKind = null;
        position.ExecutionRequestRuleIndex = null;
        position.ExecutionOrderId = null;
    }
}
