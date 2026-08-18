using StockTrader.Application.Execution;
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
    private static readonly TimeSpan MaxSignalAge = TimeSpan.FromHours(24);

    private readonly IAccountManager _accounts;
    private readonly ITradeRepository _trades;
    private readonly INotificationService _notifications;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ISignalService _signals;
    private readonly TimeProvider _timeProvider;
    private readonly IManualOrderSignalStore _signalStore;
    private readonly ILiveEntryExecutionCoordinator _entryExecutions;
    private readonly ILogger<ManualOrderWorkflow> _logger;

    public ManualOrderWorkflow(
        IAccountManager accounts,
        ITradeRepository trades,
        INotificationService notifications,
        IMarketCalendar marketCalendar,
        ISignalService signals,
        TimeProvider timeProvider,
        IManualOrderSignalStore signalStore,
        ILiveEntryExecutionCoordinator entryExecutions,
        ILogger<ManualOrderWorkflow> logger)
    {
        _accounts = accounts;
        _trades = trades;
        _notifications = notifications;
        _marketCalendar = marketCalendar;
        _signals = signals;
        _timeProvider = timeProvider;
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

        var recommendation = await CreateRecommendationAsync(signal, ct);
        var validationError = Validate(signal, recommendation);
        if (validationError is not null)
            return (false, validationError);

        var account = await _accounts.GetBrokerContextAsync(ct: ct);
        if (account is null)
        {
            _logger.LogWarning("[MANUAL ORDER] No active broker service for {Symbol}", signal.Symbol);
            return (false, "활성 브로커 계좌가 없습니다. 계좌 관리에서 계좌를 설정하세요.");
        }

        await _trades.AddRecommendationAsync(recommendation, ct);
        var execution = await _entryExecutions.ExecuteAsync(recommendation, account, ct);
        if (!execution.BrokerAccepted)
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
            _logger.LogCritical(
                "[MANUAL ORDER ACCEPTED, TRACKING FAILED] {Symbol}: "
                + "Account={AccountId} OrderId={OrderId}",
                signal.Symbol,
                account.Account.Id,
                execution.Order?.OrderId);
            return (true,
                $"{signal.Symbol} 주문은 브로커에 접수됐지만 로컬 포지션 기록에 실패했습니다. "
                + "재주문하지 말고 브로커 주문 내역을 확인하세요.");
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

    private string? Validate(PatternSignal signal, TradeRecommendation recommendation)
    {
        var nowEt = _marketCalendar.GetLocalNow(MarketType.US);
        if (!_marketCalendar.IsMarketOpen(MarketType.US))
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 장외 시간 ({Time:HH:mm} ET, {DayOfWeek})",
                signal.Symbol, nowEt, nowEt.DayOfWeek);
            return $"장외 시간입니다 (ET {nowEt:HH:mm}, {nowEt.DayOfWeek}). " +
                   "정규장(09:30–16:00 ET) 중에 다시 시도하세요.";
        }

        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning(
                "[MANUAL ORDER] {Symbol}: 주문 수량이 0입니다 (계좌 잔고 부족 가능성)", signal.Symbol);
            return $"{signal.Symbol}: 계산된 주문 수량이 0입니다. 계좌 잔고를 확인하세요.";
        }

        var signalAge = UtcNow - signal.DetectedAt;
        if (signalAge > MaxSignalAge)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 시그널이 {Age:F1}시간 전 생성됨 (한계={Limit}h)",
                signal.Symbol, signalAge.TotalHours, MaxSignalAge.TotalHours);
            return $"{signal.Symbol} 시그널이 {signalAge.TotalHours:F0}시간 전 생성됨. " +
                   "24시간 초과 시그널은 주문할 수 없습니다.";
        }

        if (recommendation.StopLossPrice >= recommendation.EntryPrice)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 손절가({SL:F2}) >= 진입가({Entry:F2}) — 시그널 무효",
                signal.Symbol, recommendation.StopLossPrice, recommendation.EntryPrice);
            return $"{signal.Symbol} 손절가({recommendation.StopLossPrice:F2})가 " +
                   $"진입가({recommendation.EntryPrice:F2}) 이상입니다. 시그널이 유효하지 않습니다.";
        }

        if (recommendation.TargetPrice <= recommendation.EntryPrice)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 목표가({Target:F2}) <= 진입가({Entry:F2}) — 시그널 무효",
                signal.Symbol, recommendation.TargetPrice, recommendation.EntryPrice);
            return $"{signal.Symbol} 목표가({recommendation.TargetPrice:F2})가 " +
                   $"진입가({recommendation.EntryPrice:F2}) 이하입니다. 시그널이 유효하지 않습니다.";
        }

        return null;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
