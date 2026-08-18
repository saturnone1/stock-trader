using Microsoft.Extensions.Options;
using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
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
/// 공통 실행 정책으로 오픈 포지션을 평가하고 내구성 있는 브로커 주문을 조정합니다.
/// </summary>
public class PositionExecutionManagerService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAccountManager _accountManager;
    private readonly INotificationService _notificationService;
    private readonly IMarketCalendar _marketCalendar;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PositionExecutionManagerService> _logger;

    private volatile PatternParameterOverrides? _liveExecutionOverrides;

    public PositionExecutionManagerService(
        IServiceScopeFactory scopeFactory,
        IAccountManager accountManager,
        INotificationService notificationService,
        IMarketCalendar marketCalendar,
        TimeProvider timeProvider,
        ILogger<PositionExecutionManagerService> logger)
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
        _logger.LogInformation("PositionExecutionManagerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_marketCalendar.IsMarketOpen(MarketType.US))
                {
                    await CheckPositionExecutionsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PositionExecutionManagerService error");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("PositionExecutionManagerService stopped");
    }

    private async Task CheckPositionExecutionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var positions = scope.ServiceProvider.GetRequiredService<IOpenPositionStore>();
        var ohlcvRepo = scope.ServiceProvider.GetRequiredService<IOhlcvRepository>();
        var liveParamService = scope.ServiceProvider.GetRequiredService<ILiveParameterService>();
        var strategies = scope.ServiceProvider.GetRequiredService<ICompiledStrategyRepository>();
        var executionCoordinator = scope.ServiceProvider.GetRequiredService<ILivePositionExecutionCoordinator>();
        var executionEvaluator = scope.ServiceProvider.GetRequiredService<LivePositionExecutionEvaluator>();
        var tradingSettings = scope.ServiceProvider
            .GetRequiredService<IOptions<TradingSettings>>().Value;

        _liveExecutionOverrides = await liveParamService.GetLiveOverridesAsync(ct);

        var openPositions = await positions.GetOpenPositionsAsync(ct);
        var customPatterns = await strategies.GetByNamesAsync(
            openPositions.Select(position => position.CustomPatternName).OfType<string>(), ct);

        if (openPositions.Count == 0) return;

        var brokerService = await _accountManager.GetActiveBrokerServiceAsync(ct);
        if (brokerService is null || !BrokerCatalog.CanMonitorPositions(brokerService.BrokerType)) return;
        var brokerAccount = await brokerService.GetAccountAsync(ct);
        var currentEquity = brokerAccount?.TotalEquity ?? 0m;

        var brokerPositions = await brokerService.GetPositionsAsync(ct);
        var brokerPriceMap = brokerPositions
            .ToDictionary(p => p.Symbol, p => p.CurrentPrice, StringComparer.OrdinalIgnoreCase);

        foreach (var position in openPositions)
        {
            try
            {
                if (position.ExecutionRequestedAt.HasValue)
                {
                    var reconciliation = await executionCoordinator.ReconcileAsync(
                        position, brokerService, ct: ct);
                    HandleExecutionReconciliation(position, reconciliation);
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
                var executionDecision = await executionEvaluator.EvaluateAsync(
                    position,
                    customStrategy,
                    ohlcvRepo,
                    _liveExecutionOverrides,
                    ct,
                    currentEquity: currentEquity,
                    maxTotalPositions: tradingSettings.MaxTotalPositions);

                if (executionDecision.ShouldExecute)
                {
                    _logger.LogInformation(
                        "[POSITION-ORDER] {Symbol} — {Reason} (Entry={Entry:F2}, Current={Current:F2}, PnL={PnL:P2})",
                        position.Symbol, executionDecision.Reason, position.EntryPrice,
                        position.CurrentPrice, position.CurrentPrice / position.EntryPrice - 1);

                    var submission = await executionCoordinator.SubmitAsync(
                        position,
                        new LivePositionExecutionRequest(
                            executionDecision.Intent!.Quantity,
                            executionDecision.Intent.Reason,
                            executionDecision.Intent.Kind,
                            executionDecision.Intent.ScalingRuleIndex,
                            executionDecision.Intent.MarksPartialProfit),
                        brokerService,
                        ct);
                    if (submission.Status != LivePositionExecutionSubmissionStatus.Accepted
                        || submission.Order is null
                        || !submission.RequestedAt.HasValue)
                        continue;

                    var reconciliation = await executionCoordinator.ReconcileAsync(
                        position, brokerService, [submission.Order], ct);
                    if (reconciliation.Status == LivePositionExecutionReconciliationStatus.AwaitingBroker)
                    {
                        reconciliation = await WaitForExecutionResolutionAsync(
                            position, brokerService, executionCoordinator, ct);
                    }
                    HandleExecutionReconciliation(position, reconciliation);
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
                        await positions.SavePositionAsync(position, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error evaluating position order for {Symbol}", position.Symbol);
            }
        }
    }

    private static async Task<LivePositionExecutionReconciliationResult> WaitForExecutionResolutionAsync(
        Position position,
        IBrokerService broker,
        ILivePositionExecutionCoordinator executionCoordinator,
        CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(500, ct);
            var reconciliation = await executionCoordinator.ReconcileAsync(position, broker, ct: ct);
            if (reconciliation.Status != LivePositionExecutionReconciliationStatus.AwaitingBroker)
                return reconciliation;
        }
        return new LivePositionExecutionReconciliationResult(
            LivePositionExecutionReconciliationStatus.AwaitingBroker);
    }

    private void HandleExecutionReconciliation(
        Position position,
        LivePositionExecutionReconciliationResult reconciliation)
    {
        if (reconciliation.Status == LivePositionExecutionReconciliationStatus.ReleasedForRetry)
        {
            _logger.LogWarning("[POSITION-ORDER] {Symbol}: 주문 {OrderId}가 {Status} 상태여서 재평가를 허용합니다.",
                position.Symbol, reconciliation.Order?.OrderId, reconciliation.Order?.Status);
            return;
        }

        if (reconciliation.Status == LivePositionExecutionReconciliationStatus.BrokerFillMismatch)
        {
            _logger.LogError(
                "[POSITION-ORDER] {Symbol}: 요청 수량 {RequestedQuantity}주와 브로커 체결 수량 {FilledQuantity}주가 다릅니다. 자동 반영을 중단합니다.",
                position.Symbol,
                position.ExecutionRequestQuantity ?? position.Quantity,
                reconciliation.FilledQuantity);
            return;
        }

        if (reconciliation.Status != LivePositionExecutionReconciliationStatus.Completed)
        {
            _logger.LogDebug("[POSITION-ORDER] {Symbol}: 주문 {OrderId}의 확정 상태를 기다립니다.",
                position.Symbol, position.ExecutionOrderId);
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
