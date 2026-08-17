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
    BrokerOrder? Order = null);

public interface ILivePositionExitCoordinator
{
    Task<LiveExitSubmission> SubmitAsync(
        Position position,
        string reason,
        IBrokerService broker,
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
        await _trades.SetPositionExitOrderIdAsync(
            position.Id, requestedAt, position.ExitOrderId, ct);
        return new LiveExitSubmission(LiveExitSubmissionStatus.Accepted, requestedAt, order);
    }
}
