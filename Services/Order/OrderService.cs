using Microsoft.EntityFrameworkCore;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.Market;
using StockTrader.Services.Notification;
using StockTrader.Services.Signal;

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
    private readonly ITradeRepository _tradeRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly INotificationService _notificationService;
    private readonly IMarketCalendar _marketCalendar;
    private readonly ISignalService _signalService;
    private readonly AppDbContext _db;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IAccountManager accountManager,
        ITradeRepository tradeRepo,
        ISettingsRepository settingsRepo,
        INotificationService notificationService,
        IMarketCalendar marketCalendar,
        ISignalService signalService,
        AppDbContext db,
        ILogger<OrderService> logger)
    {
        _accountManager = accountManager;
        _tradeRepo = tradeRepo;
        _settingsRepo = settingsRepo;
        _notificationService = notificationService;
        _marketCalendar = marketCalendar;
        _signalService = signalService;
        _db = db;
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
        await _tradeRepo.AddRecommendationAsync(recommendation, ct);
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
        var brokerService = accountId.HasValue
            ? await _accountManager.GetBrokerServiceForAccountAsync(accountId.Value, ct)
            : await _accountManager.GetActiveBrokerServiceAsync(ct);

        if (brokerService == null)
        {
            _logger.LogWarning(
                "Cannot place order for {Symbol}: no broker service available (account={AccountId})",
                recommendation.Symbol, accountId?.ToString() ?? "active");
            return false;
        }

        var success = await brokerService.PlaceOrderAsync(recommendation, ct);

        if (success)
        {
            recommendation.WasExecuted = true;
            await _tradeRepo.UpdateRecommendationAsync(recommendation, ct);

            var position = new Position
            {
                Symbol = recommendation.Symbol,
                Quantity = recommendation.ShareQuantity,
                EntryPrice = recommendation.EntryPrice,
                CurrentPrice = recommendation.EntryPrice,
                StopLossPrice = recommendation.StopLossPrice,
                TargetPrice = recommendation.TargetPrice,
                PatternType = recommendation.PatternType,
                OpenedAt = DateTime.UtcNow,
                HighSinceEntry = recommendation.EntryPrice
            };
            await _tradeRepo.SavePositionAsync(position, ct);

            _logger.LogInformation(
                "[ORDER PLACED] {Symbol}: Qty={Qty}, Entry=${Entry:F2}, Account={AccountId}",
                recommendation.Symbol, recommendation.ShareQuantity, recommendation.EntryPrice,
                accountId?.ToString() ?? "active");
        }

        return success;
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
            var positions = await brokerService.GetPositionsAsync(ct);
            if (positions.Count > 0)
                return positions;
        }

        // 브로커에서 포지션을 가져오지 못한 경우 DB 폴백
        _logger.LogDebug("Broker returned no positions, falling back to local DB");
        return await _tradeRepo.GetOpenPositionsAsync(ct);
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Message)> PlaceManualOrderAsync(
        long signalId, CancellationToken ct = default)
    {
        // 1. DB에서 PatternSignal 조회 (활성/비활성 모두 허용 — 수동 매매이므로)
        var signal = await _db.PatternSignals.FindAsync(new object[] { signalId }, ct);
        if (signal == null)
        {
            _logger.LogWarning("[MANUAL ORDER] Signal {SignalId} not found", signalId);
            return (false, $"시그널 ID {signalId}을(를) 찾을 수 없습니다.");
        }

        // 2. SignalService를 통해 리스크 체크 + TradeRecommendation 생성
        //    수동 주문도 동일한 리스크/포지션 사이징 로직을 적용한다.
        var recommendations = await _signalService.EvaluateSignalsAsync(
            new List<PatternSignal> { signal }, ct);

        TradeRecommendation recommendation;
        if (recommendations.Count == 0)
        {
            // 리스크 체크 통과 실패 시에도 최소 수량(1주)으로 수동 주문을 허용한다.
            // 사용자가 직접 결정하는 수동 매매이므로 리스크 필터를 우회한다.
            _logger.LogWarning(
                "[MANUAL ORDER] Signal {SignalId} ({Pattern} {Symbol}) failed risk check — " +
                "proceeding with qty=1 as manual override",
                signalId, signal.PatternType, signal.Symbol);

            var settings = await _settingsRepo.GetAsync(ct);
            recommendation = new TradeRecommendation
            {
                Symbol = signal.Symbol,
                PatternType = signal.PatternType,
                GeneratedAt = DateTime.UtcNow,
                EntryPrice = signal.EntryPrice,
                StopLossPrice = signal.StopLossPrice,
                TargetPrice = signal.TargetPrice,
                PositionSize = signal.EntryPrice,   // 1주 기준
                ShareQuantity = 1,
                Expectancy = 0m,
                WasExecuted = false,
                Mode = OrderMode.AutoOrder           // 수동이므로 항상 AutoOrder로 처리
            };
        }
        else
        {
            recommendation = recommendations[0];
            recommendation.Mode = OrderMode.AutoOrder; // 수동 트리거이므로 AlertOnly 무시
        }

        // 3. 장외 시간 확인 (수동 매매도 시장가 주문은 정규장에서만 체결)
        var nowEt = _marketCalendar.GetLocalNow(MarketType.US);
        if (!_marketCalendar.IsMarketOpen(MarketType.US))
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 장외 시간 ({Time:HH:mm} ET, {DayOfWeek})",
                signal.Symbol, nowEt, nowEt.DayOfWeek);
            return (false, $"장외 시간입니다 (ET {nowEt:HH:mm}, {nowEt.DayOfWeek}). 정규장(09:30–16:00 ET) 중에 다시 시도하세요.");
        }

        // 4. 수량 검증
        if (recommendation.ShareQuantity <= 0)
        {
            _logger.LogWarning("[MANUAL ORDER] {Symbol}: 주문 수량이 0입니다 (계좌 잔고 부족 가능성)",
                signal.Symbol);
            return (false, $"{signal.Symbol}: 계산된 주문 수량이 0입니다. 계좌 잔고를 확인하세요.");
        }

        // 4.5 시그널 유효성 검증 — 시장가 주문이므로 기본 논리 검증 수행
        // (진입가 감지 이후 시간 경과 + 가격 레벨 일관성 확인)
        var signalAge = DateTime.UtcNow - signal.DetectedAt;
        var maxSignalAge = TimeSpan.FromHours(24); // 일봉 시그널 기준 24시간
        if (signalAge > maxSignalAge)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 시그널이 {Age:F1}시간 전 생성됨 (한계={Limit}h)",
                signal.Symbol, signalAge.TotalHours, maxSignalAge.TotalHours);
            return (false,
                $"{signal.Symbol} 시그널이 {signalAge.TotalHours:F0}시간 전 생성됨. " +
                $"24시간 초과 시그널은 주문할 수 없습니다.");
        }

        if (recommendation.StopLossPrice >= recommendation.EntryPrice)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 손절가({SL:F2}) >= 진입가({Entry:F2}) — 시그널 무효",
                signal.Symbol, recommendation.StopLossPrice, recommendation.EntryPrice);
            return (false,
                $"{signal.Symbol} 손절가({recommendation.StopLossPrice:F2})가 " +
                $"진입가({recommendation.EntryPrice:F2}) 이상입니다. 시그널이 유효하지 않습니다.");
        }

        if (recommendation.TargetPrice <= recommendation.EntryPrice)
        {
            _logger.LogWarning(
                "[MANUAL ORDER BLOCKED] {Symbol}: 목표가({Target:F2}) <= 진입가({Entry:F2}) — 시그널 무효",
                signal.Symbol, recommendation.TargetPrice, recommendation.EntryPrice);
            return (false,
                $"{signal.Symbol} 목표가({recommendation.TargetPrice:F2})가 " +
                $"진입가({recommendation.EntryPrice:F2}) 이하입니다. 시그널이 유효하지 않습니다.");
        }

        // 5. 활성 브로커를 통한 주문 제출
        //    BUG-C01 수정: 브로커 주문 성공 후에만 DB 저장 + 알림 발송
        //    (이전 구현은 주문 전에 저장하여 실패 시 거짓 레코드가 남는 문제가 있었음)
        var brokerService = await _accountManager.GetActiveBrokerServiceAsync(ct);
        if (brokerService == null)
        {
            _logger.LogWarning("[MANUAL ORDER] No active broker service for {Symbol}", signal.Symbol);
            return (false, "활성 브로커 계좌가 없습니다. 계좌 관리에서 계좌를 설정하세요.");
        }

        try
        {
            var placed = await brokerService.PlaceOrderAsync(recommendation, ct);
            if (!placed)
            {
                _logger.LogWarning("[MANUAL ORDER FAILED] {Symbol}: 브로커가 주문을 거부했습니다", signal.Symbol);
                return (false, $"{signal.Symbol} 주문 실패: 브로커가 주문을 거부했습니다.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MANUAL ORDER FAILED] {Symbol}: 브로커 주문 실패", signal.Symbol);
            return (false, $"{signal.Symbol} 주문 실패: {ex.Message}");
        }

        // 주문 성공 후 DB 저장 + 알림 발송
        await _tradeRepo.AddRecommendationAsync(recommendation, ct);
        _notificationService.Notify(recommendation);

        recommendation.WasExecuted = true;
        await _tradeRepo.UpdateRecommendationAsync(recommendation, ct);

        var position = new Position
        {
            Symbol = recommendation.Symbol,
            Quantity = recommendation.ShareQuantity,
            EntryPrice = recommendation.EntryPrice,
            CurrentPrice = recommendation.EntryPrice,
            StopLossPrice = recommendation.StopLossPrice,
            TargetPrice = recommendation.TargetPrice,
            PatternType = recommendation.PatternType,
            OpenedAt = DateTime.UtcNow,
            HighSinceEntry = recommendation.EntryPrice
        };
        await _tradeRepo.SavePositionAsync(position, ct);

        _logger.LogInformation(
            "[MANUAL ORDER PLACED] {Symbol}: Qty={Qty}, Entry=${Entry:F2}, Pattern={Pattern}",
            recommendation.Symbol, recommendation.ShareQuantity,
            recommendation.EntryPrice, recommendation.PatternType);

        return (true, $"{recommendation.Symbol} 수동 주문 완료 (Qty={recommendation.ShareQuantity}, Entry=${recommendation.EntryPrice:F2})");
    }
}
