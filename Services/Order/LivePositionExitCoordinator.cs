using StockTrader.Application.Execution;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Broker;

namespace StockTrader.Services.Order;

public enum LiveExitSubmissionStatus
{
    Accepted,
    AlreadyPending,
    Failed,
}

public sealed record LiveExitSubmission(
    LiveExitSubmissionStatus Status,
    DateTime? RequestedAt = null,
    BrokerOrder? Order = null,
    bool BrokerOrderIdPersisted = false);

public sealed record LivePositionExitRequest(
    int Quantity,
    string Reason,
    bool MarksPartialProfit = false);

public enum LiveExitReconciliationStatus
{
    NotPending,
    AwaitingBroker,
    ReleasedForRetry,
    Completed,
    ConcurrentChange,
    BrokerFillMismatch,
}

public sealed record LiveExitReconciliationResult(
    LiveExitReconciliationStatus Status,
    BrokerOrder? Order = null,
    int FilledQuantity = 0,
    bool IsFullExit = false);

public interface ILivePositionExitCoordinator
{
    Task<LiveExitSubmission> SubmitAsync(
        Position position,
        string reason,
        IBrokerService broker,
        CancellationToken ct = default);

    Task<LiveExitSubmission> SubmitAsync(
        Position position,
        LivePositionExitRequest request,
        IBrokerService broker,
        CancellationToken ct = default);

    Task<LiveExitReconciliationResult> ReconcileAsync(
        Position position,
        IBrokerService broker,
        IReadOnlyCollection<BrokerOrder>? knownOrders = null,
        CancellationToken ct = default);
}

/// <summary>자동·수동 청산 주문을 동일한 내구성 있는 DB 청구 절차로 제출한다.</summary>
public sealed class LivePositionExitCoordinator : ILivePositionExitCoordinator
{
    private readonly ITradeRepository _trades;
    private readonly TimeProvider _timeProvider;

    public LivePositionExitCoordinator(ITradeRepository trades, TimeProvider timeProvider)
    {
        _trades = trades;
        _timeProvider = timeProvider;
    }

    public async Task<LiveExitSubmission> SubmitAsync(
        Position position,
        string reason,
        IBrokerService broker,
        CancellationToken ct = default) =>
        await SubmitAsync(
            position,
            new LivePositionExitRequest(position.Quantity, reason),
            broker,
            ct);

    public async Task<LiveExitSubmission> SubmitAsync(
        Position position,
        LivePositionExitRequest request,
        IBrokerService broker,
        CancellationToken ct = default)
    {
        if (position.ExitRequestedAt.HasValue)
            return new LiveExitSubmission(
                LiveExitSubmissionStatus.AlreadyPending, position.ExitRequestedAt);
        if (request.Quantity <= 0
            || request.Quantity > position.Quantity
            || string.IsNullOrWhiteSpace(request.Reason))
            return new LiveExitSubmission(LiveExitSubmissionStatus.Failed);

        var requestedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var claim = new PositionExitClaim(
            position.Id,
            requestedAt,
            request.Reason,
            position.Quantity,
            request.Quantity,
            request.MarksPartialProfit);
        if (!await _trades.TryClaimPositionExitAsync(claim, ct))
            return new LiveExitSubmission(LiveExitSubmissionStatus.AlreadyPending);

        position.ExitRequestedAt = requestedAt;
        position.ExitRequestReason = request.Reason;
        position.ExitRequestQuantity = request.Quantity;
        position.ExitRequestMarksPartialProfit = request.MarksPartialProfit;
        var order = request.Quantity == position.Quantity
            ? await broker.ClosePositionAsync(position.Symbol, ct)
            : await broker.ClosePositionAsync(position.Symbol, request.Quantity, ct);
        if (order is null)
        {
            await _trades.ReleasePositionExitClaimAsync(position.Id, requestedAt, ct);
            ClearExitIntent(position);
            return new LiveExitSubmission(LiveExitSubmissionStatus.Failed);
        }

        position.ExitOrderId = string.IsNullOrWhiteSpace(order.OrderId) ? null : order.OrderId;
        var orderIdPersisted = await _trades.SetPositionExitOrderIdAsync(
            position.Id, requestedAt, position.ExitOrderId, ct);
        if (!orderIdPersisted)
            position.ExitOrderId = null;
        return new LiveExitSubmission(
            LiveExitSubmissionStatus.Accepted,
            requestedAt,
            order,
            orderIdPersisted);
    }

    public async Task<LiveExitReconciliationResult> ReconcileAsync(
        Position position,
        IBrokerService broker,
        IReadOnlyCollection<BrokerOrder>? knownOrders = null,
        CancellationToken ct = default)
    {
        if (!position.ExitRequestedAt.HasValue)
            return new LiveExitReconciliationResult(LiveExitReconciliationStatus.NotPending);

        var requestedAt = position.ExitRequestedAt.Value;
        var orders = knownOrders ?? await broker.GetOrderHistoryAsync(
            requestedAt.AddSeconds(-2),
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(1),
            ct);
        var resolution = ExitOrderReconciliationPolicy.Resolve(
            position.Symbol, position.ExitOrderId, requestedAt, orders);

        if (resolution.Action == ExitOrderReconciliationAction.Wait)
        {
            return new LiveExitReconciliationResult(
                LiveExitReconciliationStatus.AwaitingBroker, resolution.Order);
        }

        if (resolution.Action == ExitOrderReconciliationAction.ReleaseForRetry)
        {
            var released = await _trades.ReleasePositionExitClaimAsync(
                position.Id, requestedAt, ct);
            if (!released)
            {
                return new LiveExitReconciliationResult(
                    LiveExitReconciliationStatus.ConcurrentChange, resolution.Order);
            }

            ClearExitIntent(position);
            return new LiveExitReconciliationResult(
                LiveExitReconciliationStatus.ReleasedForRetry, resolution.Order);
        }

        if (resolution.Order?.AverageFillPrice is not > 0)
        {
            return new LiveExitReconciliationResult(
                LiveExitReconciliationStatus.AwaitingBroker, resolution.Order);
        }

        var requestedQuantity = position.ExitRequestQuantity ?? position.Quantity;
        var filledQuantity = resolution.Order.FilledQuantity > 0
            ? resolution.Order.FilledQuantity
            : resolution.Order.Quantity > 0
                ? resolution.Order.Quantity
                : requestedQuantity;
        if (requestedQuantity <= 0 || filledQuantity != requestedQuantity)
        {
            return new LiveExitReconciliationResult(
                LiveExitReconciliationStatus.BrokerFillMismatch,
                resolution.Order,
                filledQuantity);
        }

        var exitPrice = resolution.Order.AverageFillPrice.Value;
        var exitTime = resolution.Order.FilledAt ?? _timeProvider.GetUtcNow().UtcDateTime;
        var trade = new TradeRecord
        {
            Symbol = position.Symbol,
            PatternType = position.PatternType,
            CustomPatternName = position.CustomPatternName,
            EntryPrice = position.EntryPrice,
            ExitPrice = exitPrice,
            Quantity = filledQuantity,
            EntryTime = position.OpenedAt,
            ExitTime = exitTime,
            PnL = (exitPrice - position.EntryPrice) * filledQuantity,
            PnLPercent = position.EntryPrice > 0 ? exitPrice / position.EntryPrice - 1 : 0,
            ExitReason = position.ExitRequestReason ?? "실시간 청산",
        };
        var isFullExit = filledQuantity == position.Quantity;
        var fill = new PositionExitFill(
            position.Id,
            requestedAt,
            position.Quantity,
            filledQuantity,
            exitPrice,
            exitTime,
            position.ExitOrderId,
            position.ExitRequestMarksPartialProfit);
        var completed = await _trades.TryApplyPositionExitFillAsync(fill, trade, ct);
        if (completed)
            ApplyFill(position, fill, isFullExit);
        return new LiveExitReconciliationResult(
            completed
                ? LiveExitReconciliationStatus.Completed
                : LiveExitReconciliationStatus.ConcurrentChange,
            resolution.Order,
            completed ? filledQuantity : 0,
            completed && isFullExit);
    }

    private static void ApplyFill(Position position, PositionExitFill fill, bool isFullExit)
    {
        if (isFullExit)
        {
            position.ClosedAt = fill.FilledAt;
            position.ExitPrice = fill.FillPrice;
            return;
        }

        position.Quantity -= fill.FilledQuantity;
        position.CurrentPrice = fill.FillPrice;
        position.PartialProfitTaken |= fill.MarksPartialProfit;
        ClearExitIntent(position);
    }

    private static void ClearExitIntent(Position position)
    {
        position.ExitRequestedAt = null;
        position.ExitRequestReason = null;
        position.ExitRequestQuantity = null;
        position.ExitRequestMarksPartialProfit = false;
        position.ExitOrderId = null;
    }
}
