using StockTrader.Application.Execution;
using StockTrader.Application.Trading;
using StockTrader.Models;
using StockTrader.Services.Account;

namespace StockTrader.Services.Order;

/// <summary>
/// 사용자가 요청한 청산과 진입·포지션 주문 재조정을 계좌 소유권에 맞춰 실행합니다.
/// </summary>
public sealed class LiveOrderManagement(
    IAccountManager accounts,
    IOpenPositionStore positions,
    ILivePositionExecutionCoordinator positionExecutions,
    ILiveEntryExecutionStore entryStore,
    ILiveEntryExecutionCoordinator entryExecutions,
    TimeProvider timeProvider)
    : ILiveOrderManagement
{
    public async Task<LiveOrderManagementResult> ClosePositionAsync(
        string symbol,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSymbol(symbol);
        if (normalized is null)
            return Invalid("'symbol' must not be empty.");

        var positionResult = await FindPositionAsync(normalized, "청산", ct);
        if (positionResult.Error is not null)
            return positionResult.Error;
        var position = positionResult.Position!;
        var account = await ResolvePositionAccountAsync(position, forExit: true, ct);
        if (account is null)
            return Invalid("포지션 소유 계좌의 브로커에 연결할 수 없습니다.");

        var submission = await positionExecutions.SubmitFullExitAsync(
            position, "사용자 수동 청산", account.Broker, ct);
        return submission.Status switch
        {
            LivePositionExecutionSubmissionStatus.Accepted => new(
                LiveOrderManagementFailure.None,
                submission.Status.ToString(),
                $"{normalized} 청산 주문이 브로커에 접수되었습니다.",
                Accepted: true,
                RequestedAt: submission.RequestedAt,
                BrokerStatus: submission.Order?.Status.ToString(),
                BrokerOrderIdPersisted: submission.BrokerOrderIdPersisted),
            LivePositionExecutionSubmissionStatus.AlreadyPending => new(
                LiveOrderManagementFailure.None,
                submission.Status.ToString(),
                $"{normalized} 청산 주문의 확정 상태를 기다리고 있습니다.",
                RequestedAt: submission.RequestedAt),
            LivePositionExecutionSubmissionStatus.Unsupported => Invalid(
                $"{account.Broker.BrokerType} 계좌는 전량 청산 주문을 지원하지 않습니다.",
                submission.Status.ToString()),
            _ => Invalid(
                $"{normalized} 청산 실패. 브로커 연결 상태 또는 보유 포지션을 확인하세요.",
                submission.Status.ToString()),
        };
    }

    public async Task<LiveOrderManagementResult> ReconcilePositionAsync(
        string symbol,
        CancellationToken ct = default)
    {
        var normalized = NormalizeSymbol(symbol);
        if (normalized is null)
            return Invalid("'symbol' must not be empty.");

        var positionResult = await FindPositionAsync(normalized, "재조정", ct);
        if (positionResult.Error is not null)
            return positionResult.Error;
        var position = positionResult.Position!;
        if (!position.ExecutionRequestedAt.HasValue)
        {
            return Success(
                LivePositionExecutionReconciliationStatus.NotPending.ToString(),
                $"{normalized}에는 확인할 포지션 주문이 없습니다.");
        }

        var account = await ResolvePositionAccountAsync(position, forExit: false, ct);
        if (account is null)
            return Invalid("포지션 주문 계좌에 연결할 수 없습니다.");

        var result = await positionExecutions.ReconcileAsync(
            position, account.Broker, ct: ct);
        var message = result.Status switch
        {
            LivePositionExecutionReconciliationStatus.Completed => $"{normalized} 포지션 주문 체결을 확인하고 상태 반영을 완료했습니다.",
            LivePositionExecutionReconciliationStatus.ReleasedForRetry => $"{normalized} 포지션 주문 실패가 확인되어 다시 평가할 수 있습니다.",
            LivePositionExecutionReconciliationStatus.AwaitingBroker => $"{normalized} 포지션 주문은 아직 브로커 확정 상태를 기다리고 있습니다.",
            LivePositionExecutionReconciliationStatus.ConcurrentChange => $"{normalized} 상태가 다른 작업에서 변경되어 최신 목록을 다시 불러옵니다.",
            LivePositionExecutionReconciliationStatus.BrokerFillMismatch =>
                $"{normalized} 주문 요청 수량과 브로커 체결 수량이 달라 자동 반영을 중단했습니다. 브로커 주문 내역을 확인하세요.",
            LivePositionExecutionReconciliationStatus.Unsupported =>
                $"{account.Broker.BrokerType} 계좌는 주문 내역 조회를 지원하지 않습니다.",
            _ => $"{normalized}에는 확인할 포지션 주문이 없습니다.",
        };
        return new LiveOrderManagementResult(
            LiveOrderManagementFailure.None,
            result.Status.ToString(),
            message,
            BrokerStatus: result.Order?.Status.ToString(),
            FillPrice: result.Order?.AverageFillPrice,
            FilledQuantity: result.FilledQuantity);
    }

    public async Task<LiveOrderManagementResult> ReconcileEntryAsync(
        long recommendationId,
        CancellationToken ct = default)
    {
        if (recommendationId <= 0)
            return Invalid("'recommendationId' must be positive.");

        var recommendation = await entryStore.LoadAsync(recommendationId, ct);
        if (recommendation is null)
            return NotFound("추천 내역을 찾을 수 없습니다.");
        if (!recommendation.EntryRequestedAt.HasValue)
        {
            var state = LiveEntryOrderStatusPolicy.Evaluate(
                recommendation, timeProvider.GetUtcNow().UtcDateTime);
            return Success(
                state.State.ToString(),
                recommendation.WasExecuted
                    ? "이미 체결 반영이 완료된 진입입니다."
                    : "확인할 대기 진입 주문이 없습니다.");
        }
        if (!recommendation.EntryAccountId.HasValue)
            return Conflict("대기 진입에 계좌 정보가 없어 자동 재조정을 중단했습니다.");

        var account = await accounts.GetBrokerContextForReconciliationAsync(
            recommendation.EntryAccountId.Value, ct);
        if (account is null)
            return Invalid("진입 주문 계좌에 연결할 수 없습니다.");

        var result = await entryExecutions.ReconcileAsync(recommendation, account, ct: ct);
        var message = result.Status switch
        {
            LiveEntryExecutionStatus.Completed => "브로커 체결을 확인하고 포지션 반영을 완료했습니다.",
            LiveEntryExecutionStatus.Rejected => "브로커의 최종 실패를 확인해 다시 주문할 수 있습니다.",
            LiveEntryExecutionStatus.AwaitingBroker => "브로커의 최종 체결 상태를 기다리고 있습니다.",
            LiveEntryExecutionStatus.SubmissionUnconfirmed =>
                "접수 여부가 아직 확인되지 않았습니다. 중복 주문 방지를 위해 다시 주문하지 마세요.",
            LiveEntryExecutionStatus.AmbiguousEvidence =>
                "일치 가능한 주문이 여러 개라 자동 반영을 중단했습니다. 브로커 내역을 확인하세요.",
            LiveEntryExecutionStatus.EvidenceMismatch =>
                "주문 정보가 추천과 달라 자동 반영을 중단했습니다. 브로커 내역을 확인하세요.",
            LiveEntryExecutionStatus.ConcurrentChange => "다른 작업이 상태를 변경했습니다. 목록을 새로고침하세요.",
            LiveEntryExecutionStatus.Unsupported => result.Error ?? "선택한 브로커가 이 기능을 지원하지 않습니다.",
            _ => "진입 주문 상태를 확인했습니다.",
        };
        return new LiveOrderManagementResult(
            LiveOrderManagementFailure.None,
            result.Status.ToString(),
            message,
            BrokerStatus: result.Order?.Status.ToString(),
            FillPrice: result.Order?.AverageFillPrice,
            FilledQuantity: result.Order?.FilledQuantity);
    }

    private async Task<AccountBrokerContext?> ResolvePositionAccountAsync(
        Position position,
        bool forExit,
        CancellationToken ct) => position.AccountId > 0
        ? forExit
            ? await accounts.GetBrokerContextForPositionExitAsync(position.AccountId, ct)
            : await accounts.GetBrokerContextForReconciliationAsync(position.AccountId, ct)
        : await accounts.GetBrokerContextAsync(null, ct);

    private async Task<(Position? Position, LiveOrderManagementResult? Error)> FindPositionAsync(
        string symbol,
        string operation,
        CancellationToken ct)
    {
        var matches = (await positions.GetOpenPositionsAsync(ct))
            .Where(position => position.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            0 => (null, Invalid($"{symbol}의 관리 중인 오픈 포지션을 찾을 수 없습니다.")),
            > 1 => (null, Invalid($"{symbol} 포지션이 여러 계좌에 있어 계좌별 {operation} 기능이 필요합니다.")),
            _ => (matches[0], null),
        };
    }

    private static string? NormalizeSymbol(string symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private static LiveOrderManagementResult Success(string status, string message) =>
        new(LiveOrderManagementFailure.None, status, message);

    private static LiveOrderManagementResult Invalid(string error, string? status = null) =>
        new(LiveOrderManagementFailure.InvalidRequest, status, Error: error);

    private static LiveOrderManagementResult NotFound(string error) =>
        new(LiveOrderManagementFailure.NotFound, Error: error);

    private static LiveOrderManagementResult Conflict(string error) =>
        new(LiveOrderManagementFailure.Conflict, Error: error);
}
