using StockTrader.Application.Execution;
using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Market;
using StockTrader.Services.Notification;
using StockTrader.Services.Signal;

namespace StockTrader.Services.Order;

/// <summary>
/// 사용자가 선택한 시그널을 검증하고 실제 주문·체결 확인·포지션 저장까지 수행합니다.
/// 일반 자동 주문 오케스트레이션과 분리하되 공통 진입 포지션 계약을 사용합니다.
/// </summary>
public sealed class ManualOrderWorkflow
{
    private readonly IAccountManager _accounts;
    private readonly ITradeRecommendationStore _recommendations;
    private readonly INotificationService _notifications;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ISignalService _signals;
    private readonly TimeProvider _timeProvider;
    private readonly ManualSignalEntryPolicy _entryPolicy;
    private readonly IManualOrderSignalStore _signalStore;
    private readonly ILiveEntryExecutionCoordinator _entryExecutions;
    private readonly ILogger<ManualOrderWorkflow> _logger;

    public ManualOrderWorkflow(
        IAccountManager accounts,
        ITradeRecommendationStore recommendations,
        INotificationService notifications,
        IMarketCalendar marketCalendar,
        ISignalService signals,
        TimeProvider timeProvider,
        ManualSignalEntryPolicy entryPolicy,
        IManualOrderSignalStore signalStore,
        ILiveEntryExecutionCoordinator entryExecutions,
        ILogger<ManualOrderWorkflow> logger)
    {
        _accounts = accounts;
        _recommendations = recommendations;
        _notifications = notifications;
        _marketCalendar = marketCalendar;
        _signals = signals;
        _timeProvider = timeProvider;
        _entryPolicy = entryPolicy;
        _signalStore = signalStore;
        _entryExecutions = entryExecutions;
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> ExecuteAsync(
        long signalId,
        CancellationToken ct = default)
    {
        var signal = await _signalStore.LoadAsync(signalId, ct);
        if (signal is null)
        {
            _logger.LogWarning("[MANUAL ORDER] Signal {SignalId} not found", signalId);
            return (false, $"시그널 ID {signalId}을(를) 찾을 수 없습니다.");
        }

        var signalDecision = _entryPolicy.EvaluateSignal(
            new ManualSignalEntryCandidate(
                signal.Symbol,
                signal.DetectedAt,
                signal.EntryPrice,
                signal.StopLossPrice,
                signal.TargetPrice),
            UtcNow,
            _marketCalendar.IsMarketOpen(MarketType.US),
            _marketCalendar.GetLocalNow(MarketType.US));
        if (!signalDecision.IsAllowed)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: {Code} - {Message}",
                signal.Symbol,
                signalDecision.Code,
                signalDecision.Message);
            return (false, signalDecision.Message!);
        }

        var recommendation = await CreateRecommendationAsync(signal, ct);
        recommendation.SourceSignalId = signal.Id;
        var recommendationDecision = ManualSignalEntryPolicy.EvaluateRecommendation(
            new ManualRecommendationEntryCandidate(
                signal.Symbol,
                recommendation.ShareQuantity,
                recommendation.EntryPrice,
                recommendation.StopLossPrice,
                recommendation.TargetPrice));
        if (!recommendationDecision.IsAllowed)
            return (false, recommendationDecision.Message!);

        var account = await _accounts.GetBrokerContextAsync(ct: ct);
        if (account is null)
        {
            _logger.LogWarning("[MANUAL ORDER] No active broker service for {Symbol}", signal.Symbol);
            return (false, "활성 브로커 계좌가 없습니다. 계좌 관리에서 계좌를 설정하세요.");
        }

        await _recommendations.AddRecommendationAsync(recommendation, ct);
        if (recommendation.WasExecuted)
            return (false, $"{signal.Symbol} 시그널은 이미 주문 체결이 반영됐습니다.");
        var execution = await _entryExecutions.ExecuteAsync(recommendation, account, ct);
        if (!execution.ShouldPreventRetry)
        {
            _logger.LogWarning(
                "[MANUAL ORDER FAILED] {Symbol}: 브로커가 주문을 거부했습니다", signal.Symbol);
            var reason = string.IsNullOrWhiteSpace(execution.Error)
                ? "브로커가 주문을 거부했습니다."
                : execution.Error;
            return (false, $"{signal.Symbol} 주문 실패: {reason}");
        }

        try
        {
            _notifications.Notify(recommendation);
        }
        catch (Exception exception)
        {
            // 이미 접수된 주문을 알림 실패 때문에 실패로 응답하면 사용자가 재주문할 수 있다.
            _logger.LogWarning(exception,
                "[MANUAL ORDER] Notification failed after broker acceptance: {Symbol}",
                signal.Symbol);
        }
        if (!execution.IsTracked)
        {
            if (execution.Status == LiveEntryExecutionStatus.AwaitingBroker)
            {
                return (true,
                    $"{signal.Symbol} 주문이 접수됐으며 체결 확인을 기다리고 있습니다. "
                    + "상태가 확정될 때까지 다시 주문하지 마세요.");
            }

            _logger.LogCritical(
                "[MANUAL ORDER REQUIRES RECONCILIATION] {Symbol}: "
                + "Account={AccountId} OrderId={OrderId} Status={Status}",
                signal.Symbol,
                account.Account.Id,
                execution.Order?.OrderId,
                execution.Status);
            return (true,
                $"{signal.Symbol} 주문의 접수 또는 로컬 반영 상태를 자동 확정하지 못했습니다. "
                + "재주문하지 말고 진입 주문 상태와 브로커 내역을 확인하세요.");
        }

        var position = execution.Position!;

        _logger.LogInformation(
            "[MANUAL ORDER PLACED] {Symbol}: Qty={Qty}, Entry=${Entry:F2}, Pattern={Pattern}",
            position.Symbol, position.Quantity, position.EntryPrice, position.PatternType);
        return (true,
            $"{position.Symbol} 수동 주문 완료 (Qty={position.Quantity}, Entry=${position.EntryPrice:F2})");
    }

    private async Task<TradeRecommendation> CreateRecommendationAsync(
        PatternSignal signal,
        CancellationToken ct)
    {
        var recommendations = await _signals.EvaluateSignalsAsync([signal], ct);
        if (recommendations.Count > 0)
        {
            recommendations[0].Mode = OrderMode.AutoOrder;
            return recommendations[0];
        }

        _logger.LogWarning(
            "[MANUAL ORDER] Signal {SignalId} ({Pattern} {Symbol}) failed risk check — " +
            "proceeding with qty=1 as manual override",
            signal.Id, signal.PatternType, signal.Symbol);
        return new TradeRecommendation
        {
            Symbol = signal.Symbol,
            PatternType = signal.PatternType,
            CustomPatternName = signal.CustomPatternName,
            GeneratedAt = UtcNow,
            EntryPrice = signal.EntryPrice,
            StopLossPrice = signal.StopLossPrice,
            TargetPrice = signal.TargetPrice,
            PositionSize = signal.EntryPrice,
            ShareQuantity = 1,
            Expectancy = 0m,
            WasExecuted = false,
            Mode = OrderMode.AutoOrder,
        };
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
