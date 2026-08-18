using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Services.Account;

namespace StockTrader.Services.Order;

/// <summary>
/// 자동·수동 신규 진입을 DB 선점, 브로커 주문 증거 저장, 체결 재조정, 원자적
/// 포지션 반영 순서로 실행한다. 불명확한 외부 결과는 재주문하지 않고 영속 대기시킨다.
/// </summary>
public sealed class LiveEntryExecutionCoordinator(
    ILiveEntryExecutionStore store,
    TimeProvider timeProvider,
    ILogger<LiveEntryExecutionCoordinator> logger)
    : ILiveEntryExecutionCoordinator
{
    public async Task<LiveEntryExecutionResult> ExecuteAsync(
        TradeRecommendation recommendation,
        AccountBrokerContext account,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        ArgumentNullException.ThrowIfNull(account);

        if (recommendation.Id <= 0)
            return Rejected("A recommendation must be persisted before broker submission.");
        if (recommendation.WasExecuted)
            return new LiveEntryExecutionResult(LiveEntryExecutionStatus.AlreadyCompleted);
        if (recommendation.EntryRequestedAt.HasValue)
            return new LiveEntryExecutionResult(LiveEntryExecutionStatus.AlreadyPending);
        if (!BrokerCatalog.Get(account.Account.BrokerType).Capabilities.CanSubmitProtectedEntry)
        {
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.Unsupported,
                Error: "선택한 브로커는 손절·익절 보호 주문을 포함한 신규 진입을 지원하지 않습니다.");
        }

        var requestedAt = timeProvider.GetUtcNow().UtcDateTime;
        if (!await store.TryClaimAsync(
                recommendation, account.Account.Id, requestedAt, ct))
        {
            return new LiveEntryExecutionResult(LiveEntryExecutionStatus.AlreadyPending);
        }
        ApplyClaim(recommendation, account.Account.Id, requestedAt);

        BrokerOrder? order;
        try
        {
            order = await account.Broker.SubmitEntryOrderAsync(recommendation, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            const string note = "Broker acceptance is unknown after cancellation. Do not retry automatically.";
            await NoteAsync(recommendation, requestedAt, note, CancellationToken.None);
            logger.LogCritical(
                "Entry submission was cancelled after durable claim; broker acceptance is unknown: "
                + "{Symbol} Account={AccountId} Recommendation={RecommendationId}",
                recommendation.Symbol,
                account.Account.Id,
                recommendation.Id);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.SubmissionUnconfirmed,
                Error: note);
        }
        catch (Exception exception)
        {
            const string note = "Broker submission outcome is unknown after a transport error. Do not retry automatically.";
            await NoteAsync(recommendation, requestedAt, note, CancellationToken.None);
            logger.LogCritical(exception,
                "Entry submission outcome is unknown after durable claim: {Symbol} "
                + "Account={AccountId} Recommendation={RecommendationId}",
                recommendation.Symbol,
                account.Account.Id,
                recommendation.Id);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.SubmissionUnconfirmed,
                Error: note);
        }

        if (order is null)
        {
            const string rejection = "브로커가 주문을 거부했습니다.";
            await ReleaseAsync(recommendation, requestedAt, rejection, ct);
            return Rejected(rejection);
        }

        if (!string.IsNullOrWhiteSpace(order.OrderId))
        {
            var persisted = await store.SetOrderEvidenceAsync(
                recommendation, requestedAt, order.OrderId, ct);
            if (persisted)
                recommendation.EntryOrderId = order.OrderId;
            else
            {
                logger.LogCritical(
                    "Entry order accepted but its order ID could not be persisted: "
                    + "{Symbol} Recommendation={RecommendationId} OrderId={OrderId}",
                    recommendation.Symbol,
                    recommendation.Id,
                    order.OrderId);
                return new LiveEntryExecutionResult(
                    LiveEntryExecutionStatus.ConcurrentChange,
                    order,
                    Error: "Broker order ID could not be persisted.");
            }
        }

        var evidenceError = LiveEntryOrderEvidencePolicy.ValidateAcceptedOrder(
            recommendation,
            order);
        if (evidenceError is not null)
        {
            await NoteAsync(recommendation, requestedAt, evidenceError, ct);
            logger.LogCritical(
                "Entry order evidence mismatch: {Symbol} Account={AccountId} "
                + "OrderId={OrderId} Error={Error}",
                recommendation.Symbol,
                account.Account.Id,
                order.OrderId,
                evidenceError);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.EvidenceMismatch,
                order,
                Error: evidenceError);
        }

        if (LiveEntryOrderEvidencePolicy.IsTerminalRejection(order))
        {
            await ReleaseAsync(
                recommendation,
                requestedAt,
                $"Broker returned terminal status {order.Status}.",
                ct);
            return Rejected($"Broker returned terminal status {order.Status}.", order);
        }

        return await ReconcileAsync(recommendation, account, [order], ct);
    }

    public async Task<LiveEntryExecutionResult> ReconcileAsync(
        TradeRecommendation recommendation,
        AccountBrokerContext account,
        IReadOnlyCollection<BrokerOrder>? knownOrders = null,
        CancellationToken ct = default)
    {
        if (!recommendation.EntryRequestedAt.HasValue)
        {
            return new LiveEntryExecutionResult(
                recommendation.WasExecuted
                    ? LiveEntryExecutionStatus.Completed
                    : LiveEntryExecutionStatus.Rejected);
        }
        if (recommendation.EntryAccountId != account.Account.Id)
        {
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.EvidenceMismatch,
                Error: "The pending entry belongs to a different trading account.");
        }

        if (knownOrders is null
            && !BrokerCatalog.Get(account.Account.BrokerType).Capabilities.CanReadOrderHistory)
        {
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.Unsupported,
                Error: "선택한 브로커는 주문 내역 조회를 지원하지 않습니다.");
        }

        var requestedAt = recommendation.EntryRequestedAt.Value;
        var orders = knownOrders ?? await account.Broker.GetOrderHistoryAsync(
            requestedAt.AddSeconds(-2),
            timeProvider.GetUtcNow().UtcDateTime.AddSeconds(1),
            ct);
        var resolution = EntryOrderReconciliationPolicy.Resolve(recommendation, orders);
        if (resolution.Action == EntryOrderReconciliationAction.Wait)
        {
            return new LiveEntryExecutionResult(
                string.IsNullOrWhiteSpace(recommendation.EntryOrderId)
                    ? LiveEntryExecutionStatus.SubmissionUnconfirmed
                    : LiveEntryExecutionStatus.AwaitingBroker,
                resolution.Order);
        }
        if (resolution.Action == EntryOrderReconciliationAction.Ambiguous)
        {
            await NoteAsync(
                recommendation,
                requestedAt,
                "Multiple broker orders match this entry request.",
                ct);
            return new LiveEntryExecutionResult(LiveEntryExecutionStatus.AmbiguousEvidence);
        }
        if (resolution.Action == EntryOrderReconciliationAction.EvidenceMismatch)
        {
            const string note = "Stored broker order evidence does not match the entry request.";
            await NoteAsync(recommendation, requestedAt, note, ct);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.EvidenceMismatch,
                resolution.Order,
                Error: note);
        }
        if (resolution.Action == EntryOrderReconciliationAction.ReleaseForRetry)
        {
            var released = await ReleaseAsync(
                recommendation,
                requestedAt,
                $"Broker returned terminal status {resolution.Order?.Status}.",
                ct);
            return new LiveEntryExecutionResult(
                released
                    ? LiveEntryExecutionStatus.Rejected
                    : LiveEntryExecutionStatus.ConcurrentChange,
                resolution.Order);
        }

        var order = resolution.Order!;
        var filledQuantity = order.FilledQuantity > 0
            ? order.FilledQuantity
            : order.Quantity;
        if (filledQuantity != recommendation.ShareQuantity
            || order.AverageFillPrice is not > 0)
        {
            const string note = "Broker fill quantity or average price does not match the entry request.";
            await NoteAsync(recommendation, requestedAt, note, ct);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.EvidenceMismatch,
                order,
                Error: note);
        }

        var position = LiveEntryPositionFactory.CreateFromFill(
            recommendation,
            account.Account.Id,
            filledQuantity,
            order.AverageFillPrice.Value,
            order.FilledAt ?? timeProvider.GetUtcNow().UtcDateTime);
        var committed = await store.CommitFilledEntryAsync(
            recommendation, requestedAt, position, ct);
        if (!committed)
        {
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.ConcurrentChange,
                order,
                Error: "The entry state changed before the fill could be committed.");
        }

        recommendation.WasExecuted = true;
        return new LiveEntryExecutionResult(
            LiveEntryExecutionStatus.Completed,
            order,
            position);
    }

    private async Task<bool> ReleaseAsync(
        TradeRecommendation recommendation,
        DateTime requestedAt,
        string note,
        CancellationToken ct)
    {
        var released = await store.ReleaseClaimAsync(
            recommendation, requestedAt, note, ct);
        if (released)
        {
            recommendation.EntryRequestedAt = null;
            recommendation.EntryExecutionNote = note;
        }
        return released;
    }

    private async Task NoteAsync(
        TradeRecommendation recommendation,
        DateTime requestedAt,
        string note,
        CancellationToken ct)
    {
        if (await store.SetExecutionNoteAsync(
                recommendation, requestedAt, note, ct))
            recommendation.EntryExecutionNote = note;
    }

    private static void ApplyClaim(
        TradeRecommendation recommendation,
        int accountId,
        DateTime requestedAt)
    {
        recommendation.EntryRequestedAt = requestedAt;
        recommendation.EntryAccountId = accountId;
        recommendation.EntryOrderId = null;
        recommendation.EntryExecutionNote = null;
    }

    private static LiveEntryExecutionResult Rejected(
        string error,
        BrokerOrder? order = null) => new(
            LiveEntryExecutionStatus.Rejected,
            order,
            Error: error);
}
