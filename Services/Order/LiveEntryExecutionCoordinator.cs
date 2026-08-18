using StockTrader.Application.Execution;
using StockTrader.Models;
using StockTrader.Services.Account;

namespace StockTrader.Services.Order;

public enum LiveEntryExecutionStatus
{
    Rejected,
    Completed,
    BrokerAcceptedTrackingFailed
}

public sealed record LiveEntryExecutionResult(
    LiveEntryExecutionStatus Status,
    BrokerOrder? Order = null,
    Position? Position = null,
    string? Error = null)
{
    public bool BrokerAccepted => Status != LiveEntryExecutionStatus.Rejected;
    public bool IsTracked => Status == LiveEntryExecutionStatus.Completed;
}

public interface ILiveEntryExecutionCoordinator
{
    Task<LiveEntryExecutionResult> ExecuteAsync(
        TradeRecommendation recommendation,
        AccountBrokerContext account,
        CancellationToken ct = default);
}

/// <summary>
/// 자동·수동 신규 진입의 브로커 접수, 체결 확인, 계좌 귀속, 원자적 로컬 반영을
/// 하나의 순서로 실행합니다.
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

        BrokerOrder? order;
        try
        {
            order = await account.Broker.SubmitEntryOrderAsync(recommendation, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Entry submission failed before acceptance: {Symbol} Account={AccountId}",
                recommendation.Symbol,
                account.Account.Id);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.Rejected,
                Error: exception.Message);
        }

        if (order is null)
            return new LiveEntryExecutionResult(LiveEntryExecutionStatus.Rejected);
        if (LiveEntryOrderEvidencePolicy.IsTerminalRejection(order))
        {
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.Rejected,
                order,
                Error: $"Broker returned terminal status {order.Status}.");
        }

        var evidenceError = LiveEntryOrderEvidencePolicy.ValidateAcceptedOrder(
            recommendation,
            order);
        if (evidenceError is not null)
        {
            logger.LogCritical(
                "Entry order evidence mismatch: {Symbol} Account={AccountId} "
                + "OrderId={OrderId} Error={Error}",
                recommendation.Symbol,
                account.Account.Id,
                order.OrderId,
                evidenceError);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.BrokerAcceptedTrackingFailed,
                order,
                Error: evidenceError);
        }

        try
        {
            var brokerPosition = await BrokerPositionConfirmation.WaitForAsync(
                account.Broker,
                recommendation.Symbol,
                ct);
            var position = LiveEntryPositionFactory.Create(
                recommendation,
                brokerPosition,
                account.Account.Id,
                timeProvider.GetUtcNow().UtcDateTime);
            await store.CommitAcceptedEntryAsync(recommendation, position, ct);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.Completed,
                order,
                position);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogCritical(
                "Entry order accepted but local tracking was cancelled: {Symbol} "
                + "Account={AccountId} OrderId={OrderId}",
                recommendation.Symbol,
                account.Account.Id,
                order.OrderId);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.BrokerAcceptedTrackingFailed,
                order,
                Error: "Local tracking was cancelled after broker acceptance.");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception,
                "Entry order accepted but local tracking failed: {Symbol} "
                + "Account={AccountId} OrderId={OrderId}",
                recommendation.Symbol,
                account.Account.Id,
                order.OrderId);
            return new LiveEntryExecutionResult(
                LiveEntryExecutionStatus.BrokerAcceptedTrackingFailed,
                order,
                Error: exception.Message);
        }
    }
}
