using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

internal sealed class TradingCoreLiveOrderManagement(
    ITradingCoreControlPlane core,
    ILiveDailyScanData marketData,
    TimeProvider clock) : ILiveOrderManagement
{
    public async Task<LiveOrderManagementResult> ClosePositionAsync(
        string symbol,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(symbol)
            ? null
            : symbol.Trim().ToUpperInvariant();
        if (normalized is null)
            return Invalid("'symbol' must not be empty.");
        var status = await RequireRemoteAsync(ct);
        var matches = (await core.GetPortfolioAsync(ct)).Positions
            .Where(value => value.ClosedAtUtc is null
                && value.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            return NotFound($"{normalized}의 Trading Core 오픈 포지션을 찾을 수 없습니다.");
        if (matches.Length > 1)
            return Invalid($"{normalized} 포지션이 여러 계좌에 있어 계좌별 청산이 필요합니다.");
        var position = matches[0];
        if (position.ExecutionContext is null)
            return Conflict("포지션의 불변 실행 근거가 없어 원격 청산 명령을 만들 수 없습니다.");
        var now = clock.GetUtcNow().UtcDateTime;
        var bars = await marketData.LoadBarsAsync(
            position.Symbol,
            now.AddDays(-StrategyEvaluationPolicy.LivePositionIndicatorLookbackDays),
            now,
            ct);
        if (!bars.Evidence.IsComplete)
            return Conflict("완전한 시장 데이터 증거가 없어 원격 청산 명령을 만들 수 없습니다.");
        var command = TradingCorePositionCommandFactory.Create(
            status,
            position,
            TradingPositionActionKinds.FullExit,
            position.Quantity,
            "사용자 수동 청산",
            bars.Evidence,
            now);
        var receipt = await core.SubmitPositionAsync(command, ct);
        return Receipt(receipt, $"{normalized} 청산 명령이 Trading Core에 접수됐습니다.");
    }

    public async Task<LiveOrderManagementResult> ReconcilePositionAsync(
        string symbol,
        CancellationToken ct = default)
    {
        var normalized = string.IsNullOrWhiteSpace(symbol)
            ? null
            : symbol.Trim().ToUpperInvariant();
        if (normalized is null)
            return Invalid("'symbol' must not be empty.");
        await RequireRemoteAsync(ct);
        var positions = (await core.GetPortfolioAsync(ct)).Positions
            .Where(value => value.Symbol.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => value.OpenedAtUtc)
            .ToArray();
        if (positions.Length == 0)
            return NotFound($"{normalized} 포지션을 찾을 수 없습니다.");
        var command = await core.GetLatestPositionCommandAsync(positions[0].PositionId, ct);
        return command is null
            ? Success("NotPending", $"{normalized}에는 확인할 원격 포지션 주문이 없습니다.")
            : Status(command, $"{normalized} 원격 포지션 주문 상태를 확인했습니다.");
    }

    public async Task<LiveOrderManagementResult> ReconcileEntryAsync(
        long recommendationId,
        CancellationToken ct = default)
    {
        if (recommendationId <= 0)
            return Invalid("'recommendationId' must be positive.");
        await RequireRemoteAsync(ct);
        var command = await core.GetLatestEntryCommandAsync(recommendationId.ToString(), ct);
        return command is null
            ? NotFound("추천의 원격 진입 주문을 찾을 수 없습니다.")
            : Status(command, "원격 진입 주문 상태를 확인했습니다.");
    }

    private async Task<TradingCoreStatus> RequireRemoteAsync(CancellationToken ct)
    {
        var status = await core.GetStatusAsync(ct);
        if (!status.Ready || status.Mode != TradingAuthorityMode.Remote)
            throw new InvalidOperationException("trading-core-remote-authority-unavailable");
        return status;
    }

    private static LiveOrderManagementResult Receipt(
        TradingCommandReceipt receipt,
        string message) => receipt.Status == TradingCommandStatuses.Rejected
        ? Invalid(receipt.Message, receipt.Status)
        : new LiveOrderManagementResult(
            LiveOrderManagementFailure.None,
            receipt.Status,
            message,
            Accepted: receipt.Status is TradingCommandStatuses.PendingBrokerSubmission
                or TradingCommandStatuses.AwaitingBrokerEvidence,
            RequestedAt: receipt.AcceptedAtUtc);

    private static LiveOrderManagementResult Status(
        TradingCommandStatusView command,
        string message) => new(
            LiveOrderManagementFailure.None,
            command.Status,
            message,
            Accepted: command.Status is TradingCommandStatuses.PendingBrokerSubmission
                or TradingCommandStatuses.AwaitingBrokerEvidence
                or TradingCommandStatuses.ReconciliationRequired,
            RequestedAt: command.AcceptedAtUtc,
            BrokerOrderIdPersisted: !string.IsNullOrWhiteSpace(command.BrokerOrderId));

    private static LiveOrderManagementResult Success(string status, string message) =>
        new(LiveOrderManagementFailure.None, status, message);

    private static LiveOrderManagementResult Invalid(string error, string? status = null) =>
        new(LiveOrderManagementFailure.InvalidRequest, status, Error: error);

    private static LiveOrderManagementResult NotFound(string error) =>
        new(LiveOrderManagementFailure.NotFound, Error: error);

    private static LiveOrderManagementResult Conflict(string error) =>
        new(LiveOrderManagementFailure.Conflict, Error: error);
}
