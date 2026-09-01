using StockTrader.Data.Repositories;
using StockTrader.Application.MarketData;
using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Notification;

namespace StockTrader.Services.Order;

/// <summary>
/// 주문 오케스트레이션 서비스.
///
/// 책임:
/// - 주문 모드(AlertOnly vs AutoOrder) 판단
/// - 추천 내역 DB 저장 및 알림 발송
/// - IAccountManager를 통해 계좌별 IBrokerService를 선택하여 주문 위임
/// - accountId가 null이면 활성 계좌를 사용 (기존 단일 계좌 동작 유지)
///
/// Alpaca SDK는 이 클래스에 존재하지 않는다 — IBrokerService 추상화 뒤로 완전히 숨겨짐.
/// </summary>
public class OrderService : IOrderService
{
    private readonly IAccountManager _accountManager;
    private readonly ITradeRecommendationStore _recommendations;
    private readonly ISettingsRepository _settingsRepo;
    private readonly INotificationService _notificationService;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ManualOrderWorkflow _manualOrders;
    private readonly ILiveEntryExecutionCoordinator _entryExecutions;
    private readonly IFinancialCommandGate _commandGate;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IAccountManager accountManager,
        ITradeRecommendationStore recommendations,
        ISettingsRepository settingsRepo,
        INotificationService notificationService,
        IMarketCalendar marketCalendar,
        ManualOrderWorkflow manualOrders,
        ILiveEntryExecutionCoordinator entryExecutions,
        IFinancialCommandGate commandGate,
        ILogger<OrderService> logger)
    {
        _accountManager = accountManager;
        _recommendations = recommendations;
        _settingsRepo = settingsRepo;
        _notificationService = notificationService;
        _marketCalendar = marketCalendar;
        _manualOrders = manualOrders;
        _entryExecutions = entryExecutions;
        _commandGate = commandGate;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, CancellationToken ct = default)
        => PlaceOrderAsync(recommendation, accountId: null, ct);

    /// <inheritdoc />
    public async Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, int? accountId,
        CancellationToken ct = default)
    {
        await _commandGate.EnsureOpenAsync(FinancialCommandClasses.NewEntry, ct);
        var userSettings = await _settingsRepo.GetAsync(ct);

        // 1. 항상 추천 내역 저장 및 알림 발송 (모드에 무관)
        await _recommendations.AddRecommendationAsync(recommendation, ct);
        _notificationService.Notify(recommendation);

        // 2. AlertOnly 모드: 실제 주문 없이 로그만 기록
        if (userSettings.OrderMode == OrderMode.AlertOnly)
        {
            _logger.LogInformation(
                "[ALERT ONLY] {Pattern} {Symbol}: Entry=${Entry:F2}, SL=${SL:F2}, Target=${Target:F2}, Qty={Qty}",
                recommendation.PatternType, recommendation.Symbol,
                recommendation.EntryPrice, recommendation.StopLossPrice,
                recommendation.TargetPrice, recommendation.ShareQuantity);
            return true;
        }

        // 3. 장외 시간 주문 차단 (Market Order + TimeInForce.Day는 정규장에서만 체결됨)
        var nowEt = _marketCalendar.GetLocalNow(MarketRegion.UnitedStates);
        if (!_marketCalendar.IsMarketOpen(MarketRegion.UnitedStates))
        {
            _logger.LogWarning(
                "[ORDER BLOCKED] {Pattern} {Symbol}: 장외 시간 주문 차단 (ET {Time:HH:mm}, {DayOfWeek})",
                recommendation.PatternType, recommendation.Symbol, nowEt, nowEt.DayOfWeek);
            return true; // 추천은 저장됨, 주문만 차단
        }

        // 4. AutoOrder 모드: 계좌별 브로커를 통한 실제 주문 실행
        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning("Cannot place order for {Symbol}: quantity is {Qty}",
                recommendation.Symbol, recommendation.ShareQuantity);
            return false;
        }

        // 계좌별 브로커 선택 (null이면 활성 계좌)
        var account = await _accountManager.GetBrokerContextAsync(accountId, ct);

        if (account is null)
        {
            _logger.LogWarning(
                "Cannot place order for {Symbol}: no broker service available (account={AccountId})",
                recommendation.Symbol, accountId?.ToString() ?? "active");
            return false;
        }

        var execution = await _entryExecutions.ExecuteAsync(recommendation, account, ct);
        if (!execution.ShouldPreventRetry)
            return false;
        if (execution.Status == LiveEntryExecutionStatus.AlreadyCompleted)
        {
            _logger.LogInformation(
                "[ORDER SKIPPED] Recommendation {RecommendationId} was already executed",
                recommendation.Id);
            return true;
        }
        if (!execution.IsTracked)
        {
            if (execution.Status == LiveEntryExecutionStatus.AwaitingBroker)
            {
                _logger.LogInformation(
                    "[ORDER PENDING] {Symbol}: Account={AccountId} OrderId={OrderId}",
                    recommendation.Symbol,
                    account.Account.Id,
                    execution.Order?.OrderId);
            }
            else
            {
                _logger.LogCritical(
                    "[ORDER REQUIRES RECONCILIATION] {Symbol}: Account={AccountId} "
                    + "OrderId={OrderId} Status={Status}",
                    recommendation.Symbol,
                    account.Account.Id,
                    execution.Order?.OrderId,
                    execution.Status);
            }
            return true;
        }

        _logger.LogInformation(
            "[ORDER PLACED] {Symbol}: Qty={Qty}, Entry=${Entry:F2}, Account={AccountId}",
            execution.Position!.Symbol,
            execution.Position.Quantity,
            execution.Position.EntryPrice,
            account.Account.Id);
        return true;
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Message)> PlaceManualOrderAsync(
        long signalId, CancellationToken ct = default)
    {
        await _commandGate.EnsureOpenAsync(FinancialCommandClasses.ManualCommand, ct);
        return await _manualOrders.ExecuteAsync(signalId, ct);
    }
}
