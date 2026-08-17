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

public enum LiveExitReconciliationStatus
{
    NotPending,
    AwaitingBroker,
    ReleasedForRetry,
    Completed,
    ConcurrentChange,
}

public sealed record LiveExitReconciliationResult(
    LiveExitReconciliationStatus Status,
    BrokerOrder? Order = null);

public interface ILivePositionExitCoordinator
{
    Task<LiveExitSubmission> SubmitAsync(
        Position position,
        string reason,
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
        CancellationToken ct = default)
    {
        if (position.ExitRequestedAt.HasValue)
            return new LiveExitSubmission(
                LiveExitSubmissionStatus.AlreadyPending, position.ExitRequestedAt);

        var requestedAt = _timeProvider.GetUtcNow().UtcDateTime;
        if (!await _trades.TryClaimPositionExitAsync(position.Id, requestedAt, reason, ct))
            return new LiveExitSubmission(LiveExitSubmissionStatus.AlreadyPending);

        position.ExitRequestedAt = requestedAt;
        position.ExitRequestReason = reason;
        var order = await broker.ClosePositionAsync(position.Symbol, ct);
        if (order is null)
        {
            await _trades.ReleasePositionExitClaimAsync(position.Id, requestedAt, ct);
            position.ExitRequestedAt = null;
            position.ExitRequestReason = null;
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

        var exitPrice = resolution.Order.AverageFillPrice.Value;
        var exitTime = resolution.Order.FilledAt ?? _timeProvider.GetUtcNow().UtcDateTime;
        position.ClosedAt = exitTime;
        position.ExitPrice = exitPrice;
        var trade = new TradeRecord
        {
            Symbol = position.Symbol,
            PatternType = position.PatternType,
            CustomPatternName = position.CustomPatternName,
            EntryPrice = position.EntryPrice,
            ExitPrice = exitPrice,
            Quantity = position.Quantity,
            EntryTime = position.OpenedAt,
            ExitTime = exitTime,
            PnL = (exitPrice - position.EntryPrice) * position.Quantity,
            PnLPercent = position.EntryPrice > 0 ? exitPrice / position.EntryPrice - 1 : 0,
            ExitReason = position.ExitRequestReason ?? "실시간 청산",
        };
        var completed = await _trades.TryCompletePositionExitAsync(position, trade, ct);
        return new LiveExitReconciliationResult(
            completed
                ? LiveExitReconciliationStatus.Completed
                : LiveExitReconciliationStatus.ConcurrentChange,
            resolution.Order);
    }

    private static void ClearExitIntent(Position position)
    {
        position.ExitRequestedAt = null;
        position.ExitRequestReason = null;
        position.ExitOrderId = null;
    }
}
