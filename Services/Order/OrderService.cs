using StockTrader.Data.Repositories;
using StockTrader.Application.Trading;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Market;
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
    private readonly IOpenPositionStore _positions;
    private readonly ISettingsRepository _settingsRepo;
    private readonly INotificationService _notificationService;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ManualOrderWorkflow _manualOrders;
    private readonly ILiveEntryExecutionCoordinator _entryExecutions;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IAccountManager accountManager,
        ITradeRecommendationStore recommendations,
        IOpenPositionStore positions,
        ISettingsRepository settingsRepo,
        INotificationService notificationService,
        IMarketCalendar marketCalendar,
        ManualOrderWorkflow manualOrders,
        ILiveEntryExecutionCoordinator entryExecutions,
        ILogger<OrderService> logger)
    {
        _accountManager = accountManager;
        _recommendations = recommendations;
        _positions = positions;
        _settingsRepo = settingsRepo;
        _notificationService = notificationService;
        _marketCalendar = marketCalendar;
        _manualOrders = manualOrders;
        _entryExecutions = entryExecutions;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, CancellationToken ct = default)
        => PlaceOrderAsync(recommendation, accountId: null, ct);

    /// <inheritdoc />
    public async Task<bool> PlaceOrderAsync(TradeRecommendation recommendation, int? accountId,
        CancellationToken ct = default)
    {
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
        var nowEt = _marketCalendar.GetLocalNow(MarketType.US);
        if (!_marketCalendar.IsMarketOpen(MarketType.US))
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
    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken ct = default)
    {
        var brokerService = await _accountManager.GetActiveBrokerServiceAsync(ct);
        if (brokerService == null)
        {
            _logger.LogWarning("[ORDER CANCEL] No active broker service to cancel order {OrderId}", orderId);
            return false;
        }
        if (!BrokerCatalog.Get(brokerService.BrokerType).Capabilities.CanCancelOrder)
        {
            _logger.LogWarning(
                "[ORDER CANCEL] Broker {BrokerType} does not support order cancellation",
                brokerService.BrokerType);
            return false;
        }

        var success = await brokerService.CancelOrderAsync(orderId, ct);

        if (success)
            _logger.LogInformation("[ORDER CANCELLED] OrderId={OrderId}", orderId);
        else
            _logger.LogWarning("[ORDER CANCEL FAILED] OrderId={OrderId}", orderId);

        return success;
    }

    /// <inheritdoc />
    public async Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        var brokerService = await _accountManager.GetActiveBrokerServiceAsync(ct);

        if (brokerService != null)
        {
            if (BrokerCatalog.Get(brokerService.BrokerType).Capabilities.CanReadPositions)
            {
                var positions = await brokerService.GetPositionsAsync(ct);
                if (positions.Count > 0)
                    return positions;
            }
        }

        // 브로커에서 포지션을 가져오지 못한 경우 DB 폴백
        _logger.LogDebug("Broker returned no positions, falling back to local DB");
        return await _positions.GetOpenPositionsAsync(ct);
    }

    /// <inheritdoc />
    public Task<(bool Success, string Message)> PlaceManualOrderAsync(
        long signalId, CancellationToken ct = default)
        => _manualOrders.ExecuteAsync(signalId, ct);
}
