using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Account;
using StockTrader.Services.Backtest;
using StockTrader.Services.Broker;
using StockTrader.Services.Indicators;
using StockTrader.Services.LiveParameter;
using StockTrader.Services.Market;
using StockTrader.Services.Notification;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 실시간 포지션 청산 관리 서비스.
///
/// 백테스트의 BacktestExecutionAdapter와 동일한 청산 로직을 실거래에 적용:
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
    private readonly IIndicatorService _indicators;
    private readonly IOptionsMonitor<PatternSettings> _patternSettings;
    private readonly TradingSettings _tradingSettings;
    private readonly IMarketCalendar _marketCalendar;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PositionExitManagerService> _logger;

    /// <summary>DB에 저장된 실거래 청산 파라미터 오버라이드 (캐시, 매 체크 시 갱신)</summary>
    private volatile PatternParameterOverrides? _liveExitOverrides;

    public PositionExitManagerService(
        IServiceScopeFactory scopeFactory,
        IAccountManager accountManager,
        INotificationService notificationService,
        IIndicatorService indicators,
        IOptionsMonitor<PatternSettings> patternSettings,
        IOptions<TradingSettings> tradingSettings,
        IMarketCalendar marketCalendar,
        TimeProvider timeProvider,
        ILogger<PositionExitManagerService> logger)
    {
        _scopeFactory = scopeFactory;
        _accountManager = accountManager;
        _notificationService = notificationService;
        _indicators = indicators;
        _patternSettings = patternSettings;
        _tradingSettings = tradingSettings.Value;
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
                    await ReconcilePendingExitAsync(position, brokerService, tradeRepo, ct);
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
                var exitResult = await EvaluateExitAsync(position, customStrategy, ohlcvRepo, ct);

                if (exitResult.ShouldExit)
                {
                    _logger.LogInformation(
                        "[EXIT] {Symbol} — {Reason} (Entry={Entry:F2}, Current={Current:F2}, PnL={PnL:P2})",
                        position.Symbol, exitResult.Reason, position.EntryPrice,
                        position.CurrentPrice, position.CurrentPrice / position.EntryPrice - 1);

                    var submission = await exitCoordinator.SubmitAsync(
                        position, exitResult.Reason, brokerService, ct);
                    if (submission.Status != LiveExitSubmissionStatus.Accepted
                        || submission.Order is null
                        || !submission.RequestedAt.HasValue)
                        continue;

                    var resolution = ExitOrderReconciliationPolicy.Resolve(
                        position.Symbol, position.ExitOrderId, submission.RequestedAt.Value, [submission.Order]);
                    if (resolution.Action == ExitOrderReconciliationAction.Wait)
                        resolution = await WaitForExitResolutionAsync(position, brokerService, ct);
                    await ApplyExitResolutionAsync(position, resolution, tradeRepo, ct);
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

    private async Task ReconcilePendingExitAsync(
        Position position,
        IBrokerService broker,
        ITradeRepository trades,
        CancellationToken ct)
    {
        var resolution = await ReadExitResolutionAsync(position, broker, ct);
        await ApplyExitResolutionAsync(position, resolution, trades, ct);
    }

    private async Task<ExitOrderReconciliation> WaitForExitResolutionAsync(
        Position position,
        IBrokerService broker,
        CancellationToken ct)
    {
        const int maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await Task.Delay(500, ct);
            var resolution = await ReadExitResolutionAsync(position, broker, ct);
            if (resolution.Action != ExitOrderReconciliationAction.Wait)
                return resolution;
        }
        return new ExitOrderReconciliation(ExitOrderReconciliationAction.Wait);
    }

    private async Task<ExitOrderReconciliation> ReadExitResolutionAsync(
        Position position,
        IBrokerService broker,
        CancellationToken ct)
    {
        var requestedAt = position.ExitRequestedAt!.Value;
        var orders = await broker.GetOrderHistoryAsync(
            requestedAt.AddSeconds(-2), UtcNow.AddSeconds(1), ct);
        return ExitOrderReconciliationPolicy.Resolve(
            position.Symbol, position.ExitOrderId, requestedAt, orders);
    }

    private async Task ApplyExitResolutionAsync(
        Position position,
        ExitOrderReconciliation resolution,
        ITradeRepository trades,
        CancellationToken ct)
    {
        if (resolution.Action == ExitOrderReconciliationAction.ReleaseForRetry)
        {
            _logger.LogWarning("[EXIT] {Symbol}: 청산 주문 {OrderId}가 {Status} 상태여서 재평가를 허용합니다.",
                position.Symbol, resolution.Order?.OrderId, resolution.Order?.Status);
            var requestedAt = position.ExitRequestedAt!.Value;
            ClearExitIntent(position);
            await trades.ReleasePositionExitClaimAsync(
                position.Id, requestedAt, ct);
            return;
        }

        if (resolution.Action != ExitOrderReconciliationAction.Finalize || resolution.Order is null)
        {
            _logger.LogDebug("[EXIT] {Symbol}: 청산 주문 {OrderId}의 확정 상태를 기다립니다.",
                position.Symbol, position.ExitOrderId);
            return;
        }

        var exitPrice = resolution.Order.AverageFillPrice!.Value;
        var exitTime = resolution.Order.FilledAt ?? UtcNow;
        position.ClosedAt = exitTime;
        position.ExitPrice = exitPrice;
        var trade = new TradeRecord
        {
            Symbol = position.Symbol,
            PatternType = position.PatternType,
            CustomPatternName = position.CustomPatternName,
            EntryPrice = position.EntryPrice,
            ExitPrice = exitPrice,
            Quantity = position.Quantity,
            EntryTime = position.OpenedAt,
            ExitTime = exitTime,
            PnL = (exitPrice - position.EntryPrice) * position.Quantity,
            PnLPercent = exitPrice / position.EntryPrice - 1,
            ExitReason = position.ExitRequestReason ?? "실시간 청산",
        };
        var completed = await trades.TryCompletePositionExitAsync(position, trade, ct);
        if (!completed)
            return;

        _notificationService.Notify(new TradeRecommendation
        {
            Symbol = position.Symbol,
            PatternType = position.PatternType,
            CustomPatternName = position.CustomPatternName,
            EntryPrice = position.EntryPrice,
            TargetPrice = exitPrice,
            ShareQuantity = position.Quantity,
            GeneratedAt = UtcNow,
        });
    }

    private static void ClearExitIntent(Position position)
    {
        position.ExitRequestedAt = null;
        position.ExitRequestReason = null;
        position.ExitOrderId = null;
    }

    private async Task<(bool ShouldExit, string Reason)> EvaluateExitAsync(
        Position position, CompiledStrategy? customStrategy, IOhlcvRepository ohlcvRepo, CancellationToken ct)
    {
        var customPattern = customStrategy?.Source;
        var pep = customPattern == null
            ? BacktestExecutionAdapter.PatternExitProfile.For(position.PatternType, _liveExitOverrides)
            : new BacktestExecutionAdapter.PatternExitProfile(
                customPattern.MaxHoldingBars,
                customPattern.TrailingAtr > 0,
                customPattern.TrailingAtr,
                1.0m,
                customPattern.PartialProfitR > 0,
                customPattern.PartialProfitR,
                true,
                true);
        var effectivePatternSettings = _liveExitOverrides == null
            ? _patternSettings.CurrentValue
            : PatternOverrideMerger.Merge(_patternSettings.CurrentValue, _liveExitOverrides);
        var cumulativeRsi2Config = effectivePatternSettings.CumulativeRsi2;
        List<OhlcvBar>? recentBars = null;
        decimal currentCumulativeRsi2 = 0;
        decimal currentCumulativeRsi2TrendMa = 0;

        // ATR 계산: DB에 저장된 EntryAtr 우선 사용, 없으면 최근 봉에서 계산.
        // ATR(14)는 15개 봉(TR 14개 + 이전 종가 1개)이 필요하다.
        // 20일 달력 기간으로 조회하면 주말·공휴일 제외 시 실질 영업일이 14개 이하가 되어
        // ATR이 0으로 반환될 위험이 있다. 30일로 확장하면 약 21영업일을 확보할 수 있다.
        var atr = position.EntryAtr;
        var needsIndicatorBars = customPattern != null
            || position.PatternType == PatternType.CumulativeRsi2
            || atr <= 0
            || (pep.EnableTimeExit && pep.MaxHoldingBars > 0);
        if (needsIndicatorBars)
        {
            recentBars = await ohlcvRepo.GetBarsAsync(position.Symbol, TimeFrame.Daily,
                UtcNow.AddDays(-400), UtcNow, ct);
            if (atr <= 0 && recentBars.Count >= 15)
                atr = CalculateSimpleAtr(recentBars, 14);
        }

        if (position.PatternType == PatternType.CumulativeRsi2 && recentBars is { Count: > 0 })
        {
            var barsArray = recentBars.ToArray();
            var closes = IndicatorService.ExtractCloses(barsArray);
            var cumulativeRsi2 = _indicators.CumulativeRsi(
                closes, cumulativeRsi2Config.RsiPeriod, cumulativeRsi2Config.CumulativePeriod);
            var trendMa = _indicators.SMA(closes, cumulativeRsi2Config.LongTrendMaPeriod);

            if (cumulativeRsi2.Length > 0)
                currentCumulativeRsi2 = cumulativeRsi2[^1];
            if (trendMa.Length > 0)
                currentCumulativeRsi2TrendMa = trendMa[^1];
        }

        StrategyExitInstruction? strategyExit = null;
        if (position.PatternType == PatternType.CumulativeRsi2
            && currentCumulativeRsi2TrendMa > 0
            && position.CurrentPrice <= currentCumulativeRsi2TrendMa)
        {
            strategyExit = new StrategyExitInstruction(
                position.CurrentPrice,
                $"{cumulativeRsi2Config.LongTrendMaPeriod}SMA 이탈");
        }
        else if (customStrategy != null && recentBars is { Count: >= 50 })
        {
            var detector = new RuleBasedDetector(_indicators, customStrategy);
            detector.SetReferenceData(await LoadReferenceDataAsync(customStrategy, position.Symbol, recentBars, ohlcvRepo, ct), UtcNow);
            if (detector.ShouldExit(recentBars.ToArray()))
                strategyExit = new StrategyExitInstruction(position.CurrentPrice, $"{customStrategy.Name} 매도 조건 충족");
        }
        else if (position.PatternType == PatternType.CumulativeRsi2
            && currentCumulativeRsi2 >= cumulativeRsi2Config.ExitThreshold)
        {
            strategyExit = new StrategyExitInstruction(
                position.CurrentPrice,
                $"누적 RSI 청산({currentCumulativeRsi2:F1})");
        }

        var stopDistance = Math.Abs(position.EntryPrice - position.StopLossPrice);
        if (stopDistance <= 0)
            stopDistance = atr > 0 ? atr : position.EntryPrice * 0.02m;
        if (position.InitialRiskDistance <= 0)
            position.InitialRiskDistance = stopDistance;
        var policy = new LongPositionExitPolicy(
            pep.MaxHoldingBars,
            pep.EnableTrailingStop,
            pep.TrailingStopAtrMultiplier,
            pep.TrailingActivationR,
            EnablePartialProfit: false,
            PartialProfitRMultiple: 0m,
            pep.EnableTargetExit,
            pep.EnableTimeExit,
            pep.BreakevenAtrMultiplier);
        var timeExitReached = recentBars is not null
            && HoldingPeriodPolicy.HasReachedDailyBarLimit(
                position.OpenedAt, recentBars, pep.MaxHoldingBars);
        var decision = LiveLongPositionDecisionPolicy.Evaluate(
            new LongPositionExecutionState(
                position.EntryPrice,
                position.StopLossPrice,
                position.TargetPrice,
                Math.Max(position.HighSinceEntry, position.EntryPrice),
                position.EntryPrice,
                position.InitialRiskDistance,
                position.EntryAtr > 0 ? position.EntryAtr : atr,
                EntryBarIndex: 0,
                position.Quantity,
                BreakevenApplied: position.BreakevenApplied,
                TrailingActivated: position.TrailingStopActivated),
            position.CurrentPrice,
            atr,
            policy,
            timeExitReached,
            strategyExit);

        position.HighSinceEntry = decision.State.HighestPrice;
        position.StopLossPrice = decision.State.StopPrice;
        position.BreakevenApplied = decision.State.BreakevenApplied;
        position.TrailingStopActivated = decision.State.TrailingActivated;
        if (decision.StopUpdate is not null)
            _logger.LogDebug("[EXIT-MGR] {Symbol}: {Reason} {Price:F2}",
                position.Symbol, decision.StopUpdate.Reason, decision.StopUpdate.Price);

        return (decision.ShouldExit, decision.Reason);
    }

    private async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceDataAsync(
        CompiledStrategy strategy,
        string symbol,
        List<OhlcvBar> symbolBars,
        IOhlcvRepository repository,
        CancellationToken ct)
    {
        var result = new Dictionary<string, OhlcvBar[]>(StringComparer.OrdinalIgnoreCase)
        {
            [symbol] = symbolBars.ToArray()
        };
        foreach (var referenceSymbol in strategy.ReferenceSymbols.Where(value => !value.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            result[referenceSymbol] = (await repository.GetBarsAsync(referenceSymbol, TimeFrame.Daily,
                    UtcNow.AddDays(-400), UtcNow, ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();
        }
        return result;
    }

    private static decimal CalculateSimpleAtr(List<OhlcvBar> bars, int period)
    {
        if (bars.Count < period + 1) return 0;

        var trueRanges = new List<decimal>();
        for (int i = bars.Count - period; i < bars.Count; i++)
        {
            var high = bars[i].High;
            var low = bars[i].Low;
            var prevClose = bars[i - 1].Close;
            var tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            trueRanges.Add(tr);
        }

        return trueRanges.Average();
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;
}
