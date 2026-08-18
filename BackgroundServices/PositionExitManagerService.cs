using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Broker;
using StockTrader.Services.LiveParameter;
using StockTrader.Services.Market;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 실시간 포지션 청산 관리 서비스.
///
/// 미리보기·백테스트와 공유하는 LongPositionExitPolicy를 실거래 판단에 적용:
/// - 트레일링 스탑 (Chandelier exit)
/// - 손익분기 스탑 (breakeven)
/// - 시간 기반 청산 (최대 보유 봉수)
/// - 목표가 청산
///
/// 매 분마다 오픈 포지션을 확인하고, 청산 조건 충족 시 브로커를 통해 포지션을 청산.
/// </summary>
public class PositionExitManagerService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAccountManager _accountManager;
    private readonly INotificationService _notificationService;
    private readonly IMarketCalendar _marketCalendar;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PositionExitManagerService> _logger;

    /// <summary>DB에 저장된 실거래 청산 파라미터 오버라이드 (캐시, 매 체크 시 갱신)</summary>
    private volatile PatternParameterOverrides? _liveExitOverrides;

    public PositionExitManagerService(
        IServiceScopeFactory scopeFactory,
        IAccountManager accountManager,
        INotificationService notificationService,
        IMarketCalendar marketCalendar,
        TimeProvider timeProvider,
        ILogger<PositionExitManagerService> logger)
    {
        _scopeFactory = scopeFactory;
        _accountManager = accountManager;
        _notificationService = notificationService;
        _marketCalendar = marketCalendar;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PositionExitManagerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_marketCalendar.IsMarketOpen(MarketType.US))
                {
                    await CheckExitConditionsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PositionExitManagerService error");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("PositionExitManagerService stopped");
    }

    private async Task CheckExitConditionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var tradeRepo = scope.ServiceProvider.GetRequiredService<ITradeRepository>();
        var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        var liveParamService = scope.ServiceProvider.GetRequiredService<ILiveParameterService>();
        var strategies = scope.ServiceProvider.GetRequiredService<ICompiledStrategyRepository>();
        var exitCoordinator = scope.ServiceProvider.GetRequiredService<ILivePositionExitCoordinator>();
        var exitEvaluator = scope.ServiceProvider.GetRequiredService<LivePositionExitEvaluator>();

        // DB에서 저장된 청산 파라미터 오버라이드 로드 (매 체크마다 갱신)
        _liveExitOverrides = await liveParamService.GetLiveOverridesAsync(ct);

        var openPositions = await tradeRepo.GetOpenPositionsAsync(ct);
        var customPatterns = await strategies.GetByNamesAsync(
            openPositions.Select(position => position.CustomPatternName).OfType<string>(), ct);

        if (openPositions.Count == 0) return;

        var brokerService = await _accountManager.GetActiveBrokerServiceAsync(ct);
        if (brokerService == null) return;

        // 브로커에서 현재 포지션 가져와서 현재가 업데이트
        var brokerPositions = await brokerService.GetPositionsAsync(ct);
        var brokerPriceMap = brokerPositions
            .ToDictionary(p => p.Symbol, p => p.CurrentPrice, StringComparer.OrdinalIgnoreCase);

        foreach (var position in openPositions)
        {
            try
            {
                if (position.ExitRequestedAt.HasValue)
                {
                    var reconciliation = await exitCoordinator.ReconcileAsync(
                        position, brokerService, ct: ct);
                    HandleExitReconciliation(position, reconciliation);
                    continue;
                }

                // 현재가 업데이트
                if (brokerPriceMap.TryGetValue(position.Symbol, out var currentPrice) && currentPrice > 0)
                    position.CurrentPrice = currentPrice;

                if (position.CurrentPrice <= 0) continue;

                var highBefore = position.HighSinceEntry;
                var stopBefore = position.StopLossPrice;
                var riskBefore = position.InitialRiskDistance;
                var breakevenBefore = position.BreakevenApplied;
                var trailingBefore = position.TrailingStopActivated;

                customPatterns.TryGetValue(position.CustomPatternName ?? string.Empty, out var customStrategy);
                var exitResult = await exitEvaluator.EvaluateAsync(
                    position, customStrategy, ohlcvRepo, _liveExitOverrides, ct);

                if (exitResult.ShouldExit)
                {
                    _logger.LogInformation(
                        "[EXIT] {Symbol} — {Reason} (Entry={Entry:F2}, Current={Current:F2}, PnL={PnL:P2})",
                        position.Symbol, exitResult.Reason, position.EntryPrice,
                        position.CurrentPrice, position.CurrentPrice / position.EntryPrice - 1);

                    var submission = await exitCoordinator.SubmitAsync(
                        position,
                        new LivePositionExitRequest(
                            exitResult.Intent!.Quantity,
                            exitResult.Intent.Reason,
                            exitResult.Intent.MarksPartialProfit),
                        brokerService,
                        ct);
                    if (submission.Status != LiveExitSubmissionStatus.Accepted
                        || submission.Order is null
                        || !submission.RequestedAt.HasValue)
                        continue;

                    var reconciliation = await exitCoordinator.ReconcileAsync(
                        position, brokerService, [submission.Order], ct);
                    if (reconciliation.Status == LiveExitReconciliationStatus.AwaitingBroker)
                    {
                        reconciliation = await WaitForExitResolutionAsync(
                            position, brokerService, exitCoordinator, ct);
                    }
                    HandleExitReconciliation(position, reconciliation);
                }
                else
                {
                    // HighSinceEntry 또는 StopLossPrice(트레일링/손익분기)가 실제로 변경된
                    // 경우에만 저장하여 불필요한 UPDATE를 제거한다.
                    var stateChanged = position.HighSinceEntry != highBefore
                                   || position.StopLossPrice != stopBefore
                                   || position.InitialRiskDistance != riskBefore
                                   || position.BreakevenApplied != breakevenBefore
                                   || position.TrailingStopActivated != trailingBefore;
                    if (stateChanged)
                    {
                        await tradeRepo.SavePositionAsync(position, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating exit for {Symbol}", position.Symbol);
            }
        }
    }

    private static async Task<LiveExitReconciliationResult> WaitForExitResolutionAsync(
        Position position,
        IBrokerService broker,
        ILivePositionExitCoordinator exitCoordinator,
        CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(500, ct);
            var reconciliation = await exitCoordinator.ReconcileAsync(position, broker, ct: ct);
            if (reconciliation.Status != LiveExitReconciliationStatus.AwaitingBroker)
                return reconciliation;
        }
        return new LiveExitReconciliationResult(LiveExitReconciliationStatus.AwaitingBroker);
    }

    private void HandleExitReconciliation(
        Position position,
        LiveExitReconciliationResult reconciliation)
    {
        if (reconciliation.Status == LiveExitReconciliationStatus.ReleasedForRetry)
        {
            _logger.LogWarning("[EXIT] {Symbol}: 청산 주문 {OrderId}가 {Status} 상태여서 재평가를 허용합니다.",
                position.Symbol, reconciliation.Order?.OrderId, reconciliation.Order?.Status);
            return;
        }

        if (reconciliation.Status == LiveExitReconciliationStatus.BrokerFillMismatch)
        {
            _logger.LogError(
                "[EXIT] {Symbol}: 요청 수량 {RequestedQuantity}주와 브로커 체결 수량 {FilledQuantity}주가 다릅니다. 자동 반영을 중단합니다.",
                position.Symbol,
                position.ExitRequestQuantity ?? position.Quantity,
                reconciliation.FilledQuantity);
            return;
        }

        if (reconciliation.Status != LiveExitReconciliationStatus.Completed)
        {
            _logger.LogDebug("[EXIT] {Symbol}: 청산 주문 {OrderId}의 확정 상태를 기다립니다.",
                position.Symbol, position.ExitOrderId);
            return;
        }

        _notificationService.Notify(new TradeRecommendation
        {
            Symbol = position.Symbol,
            PatternType = position.PatternType,
            CustomPatternName = position.CustomPatternName,
            EntryPrice = position.EntryPrice,
            TargetPrice = position.ExitPrice ?? position.CurrentPrice,
            ShareQuantity = reconciliation.FilledQuantity,
            GeneratedAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
    }

}
