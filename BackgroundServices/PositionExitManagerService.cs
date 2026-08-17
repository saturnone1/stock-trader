using System.Collections.Concurrent;
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
using StockTrader.Services.Notification;
using StockTrader.Services.Patterns;
using TimeZoneConverter;

namespace StockTrader.BackgroundServices;

/// <summary>
/// 실시간 포지션 청산 관리 서비스.
///
/// 백테스트의 TradeSimulator와 동일한 청산 로직을 실거래에 적용:
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
    private readonly ILogger<PositionExitManagerService> _logger;

    /// <summary>
    /// 포지션별 트레일링 상태 추적.
    /// Position.Id → 트레일링 스탑 활성화 여부, 손익분기 적용 여부.
    /// </summary>
    private readonly ConcurrentDictionary<long, ExitState> _exitStates = new();

    /// <summary>DB에 저장된 실거래 청산 파라미터 오버라이드 (캐시, 매 체크 시 갱신)</summary>
    private volatile PatternParameterOverrides? _liveExitOverrides;

    public PositionExitManagerService(
        IServiceScopeFactory scopeFactory,
        IAccountManager accountManager,
        INotificationService notificationService,
        IIndicatorService indicators,
        IOptionsMonitor<PatternSettings> patternSettings,
        IOptions<TradingSettings> tradingSettings,
        ILogger<PositionExitManagerService> logger)
    {
        _scopeFactory = scopeFactory;
        _accountManager = accountManager;
        _notificationService = notificationService;
        _indicators = indicators;
        _patternSettings = patternSettings;
        _tradingSettings = tradingSettings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PositionExitManagerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 미국 장 시간 체크 (ET 09:30~16:00)
                var nowEt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TZConvert.GetTimeZoneInfo("America/New_York"));

                var marketOpen = new TimeOnly(9, 30);
                var marketClose = new TimeOnly(16, 0);
                var currentTime = TimeOnly.FromDateTime(nowEt);

                if (currentTime >= marketOpen && currentTime <= marketClose
                    && nowEt.DayOfWeek != DayOfWeek.Saturday && nowEt.DayOfWeek != DayOfWeek.Sunday)
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

        // DB에서 저장된 청산 파라미터 오버라이드 로드 (매 체크마다 갱신)
        _liveExitOverrides = await liveParamService.GetLiveOverridesAsync(ct);

        var openPositions = await tradeRepo.GetOpenPositionsAsync(ct);
        var customPatterns = await strategies.GetByNamesAsync(
            openPositions.Select(position => position.CustomPatternName).OfType<string>(), ct);

        // Remove stale exit states for positions that are no longer open.
        var openIds = new HashSet<long>(openPositions.Select(p => p.Id));
        foreach (var key in _exitStates.Keys.Where(k => !openIds.Contains(k)).ToList())
            _exitStates.TryRemove(key, out _);

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
                // 현재가 업데이트
                if (brokerPriceMap.TryGetValue(position.Symbol, out var currentPrice) && currentPrice > 0)
                    position.CurrentPrice = currentPrice;

                if (position.CurrentPrice <= 0) continue;

                var highBefore = position.HighSinceEntry;
                var stopBefore = position.StopLossPrice;

                customPatterns.TryGetValue(position.CustomPatternName ?? string.Empty, out var customStrategy);
                var exitResult = await EvaluateExitAsync(position, customStrategy, ohlcvRepo, ct);

                if (exitResult.ShouldExit)
                {
                    _logger.LogInformation(
                        "[EXIT] {Symbol} — {Reason} (Entry={Entry:F2}, Current={Current:F2}, PnL={PnL:P2})",
                        position.Symbol, exitResult.Reason, position.EntryPrice,
                        position.CurrentPrice, position.CurrentPrice / position.EntryPrice - 1);

                    var closeTime = DateTime.UtcNow;
                    var closed = await brokerService.ClosePositionAsync(position.Symbol, ct);
                    if (closed)
                    {
                        // 브로커 체결가 조회 시도: 시장가 주문은 수초 내 체결되므로
                        // 짧은 대기 후 최근 주문 내역에서 AverageFillPrice를 가져온다.
                        // 조회 실패 또는 미체결 상태면 마지막 폴링 가격으로 폴백한다.
                        var fillPrice = await TryGetFillPriceAsync(
                            brokerService, position.Symbol, closeTime, ct);

                        position.ClosedAt = closeTime;
                        position.ExitPrice = fillPrice > 0 ? fillPrice : position.CurrentPrice;
                        await tradeRepo.SavePositionAsync(position, ct);

                        // 거래 기록 저장
                        var exitPx = position.ExitPrice ?? position.CurrentPrice;
                        var trade = new TradeRecord
                        {
                            Symbol = position.Symbol,
                            PatternType = position.PatternType,
                            CustomPatternName = position.CustomPatternName,
                            EntryPrice = position.EntryPrice,
                            ExitPrice = exitPx,
                            Quantity = position.Quantity,
                            EntryTime = position.OpenedAt,
                            ExitTime = closeTime,
                            PnL = (exitPx - position.EntryPrice) * position.Quantity,
                            PnLPercent = exitPx / position.EntryPrice - 1,
                            ExitReason = exitResult.Reason
                        };
                        await tradeRepo.AddTradeAsync(trade, ct);

                        // 상태 정리
                        _exitStates.TryRemove(position.Id, out _);

                        _notificationService.Notify(new TradeRecommendation
                        {
                            Symbol = position.Symbol,
                            PatternType = position.PatternType,
                            CustomPatternName = position.CustomPatternName,
                            EntryPrice = position.EntryPrice,
                            TargetPrice = position.CurrentPrice,
                            ShareQuantity = position.Quantity,
                            GeneratedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    // HighSinceEntry 또는 StopLossPrice(트레일링/손익분기)가 실제로 변경된
                    // 경우에만 저장하여 불필요한 UPDATE를 제거한다.
                    var stateChanged = position.HighSinceEntry != highBefore
                                   || position.StopLossPrice != stopBefore;
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

    private async Task<(bool ShouldExit, string Reason)> EvaluateExitAsync(
        Position position, CompiledStrategy? customStrategy, IOhlcvRepository ohlcvRepo, CancellationToken ct)
    {
        var customPattern = customStrategy?.Source;
        var pep = customPattern == null
            ? TradeSimulator.PatternExitProfile.For(position.PatternType, _liveExitOverrides)
            : new TradeSimulator.PatternExitProfile(
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
        var state = _exitStates.GetOrAdd(position.Id, _ => new ExitState());
        List<OhlcvBar>? recentBars = null;
        decimal currentCumulativeRsi2 = 0;
        decimal currentCumulativeRsi2TrendMa = 0;

        // ATR 계산: DB에 저장된 EntryAtr 우선 사용, 없으면 최근 봉에서 계산.
        // ATR(14)는 15개 봉(TR 14개 + 이전 종가 1개)이 필요하다.
        // 20일 달력 기간으로 조회하면 주말·공휴일 제외 시 실질 영업일이 14개 이하가 되어
        // ATR이 0으로 반환될 위험이 있다. 30일로 확장하면 약 21영업일을 확보할 수 있다.
        var atr = position.EntryAtr;
        var needsIndicatorBars = customPattern != null || position.PatternType == PatternType.CumulativeRsi2 || atr <= 0;
        if (needsIndicatorBars)
        {
            recentBars = await ohlcvRepo.GetBarsAsync(position.Symbol, TimeFrame.Daily,
                DateTime.UtcNow.AddDays(-400), DateTime.UtcNow, ct);
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

        var stopDistance = Math.Abs(position.EntryPrice - position.StopLossPrice);
        if (stopDistance <= 0) stopDistance = atr > 0 ? atr : position.EntryPrice * 0.02m;

        // 1. HighSinceEntry 업데이트
        if (position.CurrentPrice > position.HighSinceEntry)
            position.HighSinceEntry = position.CurrentPrice;

        // 2. 손익분기 스탑
        if (!state.BreakevenApplied && pep.BreakevenAtrMultiplier > 0 && atr > 0)
        {
            var breakevenThreshold = position.EntryPrice + atr * pep.BreakevenAtrMultiplier;
            if (position.CurrentPrice >= breakevenThreshold)
            {
                position.StopLossPrice = Math.Max(position.StopLossPrice, position.EntryPrice);
                state.BreakevenApplied = true;
                _logger.LogDebug("[EXIT-MGR] {Symbol}: breakeven stop activated at {Price:F2}",
                    position.Symbol, position.EntryPrice);
            }
        }

        // 3. 트레일링 스탑 (Chandelier)
        if (pep.EnableTrailingStop)
        {
            var activationTarget = position.EntryPrice + stopDistance * pep.TrailingActivationR;
            if (!state.TrailingActivated && position.CurrentPrice >= activationTarget)
            {
                state.TrailingActivated = true;
                _logger.LogDebug("[EXIT-MGR] {Symbol}: trailing stop activated at {Price:F2}",
                    position.Symbol, position.CurrentPrice);
            }

            if (state.TrailingActivated && atr > 0 && position.HighSinceEntry > 0)
            {
                var chandelier = position.HighSinceEntry - atr * pep.TrailingStopAtrMultiplier;
                if (chandelier > position.StopLossPrice)
                    position.StopLossPrice = chandelier;
            }
        }

        // 4. 손절 체크
        if (position.CurrentPrice <= position.StopLossPrice)
        {
            var reason = state.BreakevenApplied || state.TrailingActivated
                ? "트레일링 손절" : "손절";
            return (true, reason);
        }

        if (position.PatternType == PatternType.CumulativeRsi2
            && currentCumulativeRsi2TrendMa > 0
            && position.CurrentPrice <= currentCumulativeRsi2TrendMa)
        {
            return (true, $"{cumulativeRsi2Config.LongTrendMaPeriod}SMA 이탈");
        }

        if (customStrategy != null && recentBars is { Count: >= 50 })
        {
            var detector = new RuleBasedDetector(_indicators, customStrategy);
            detector.SetReferenceData(await LoadReferenceDataAsync(customStrategy, position.Symbol, recentBars, ohlcvRepo, ct), DateTime.UtcNow);
            if (detector.ShouldExit(recentBars.ToArray()))
                return (true, $"{customStrategy.Name} 매도 조건 충족");
        }

        if (position.PatternType == PatternType.CumulativeRsi2
            && currentCumulativeRsi2 >= cumulativeRsi2Config.ExitThreshold)
        {
            return (true, $"누적 RSI 청산({currentCumulativeRsi2:F1})");
        }

        // 5. 목표가 체크
        if (pep.EnableTargetExit && position.TargetPrice > 0
            && position.CurrentPrice >= position.TargetPrice)
        {
            return (true, "목표 도달");
        }

        // 6. 시간 기반 청산 (일봉 기준 보유일수)
        if (pep.EnableTimeExit)
        {
            var holdingDays = (DateTime.UtcNow - position.OpenedAt).TotalDays;
            // MaxHoldingBars는 일봉 기준 (주말 제외하면 약 5영업일 = 7일)
            var maxDays = pep.MaxHoldingBars * 7.0 / 5.0; // 영업일 → 달력일 변환
            if (holdingDays >= maxDays)
            {
                return (true, $"시간 청산({pep.MaxHoldingBars}봉)");
            }
        }

        return (false, string.Empty);
    }

    private static async Task<Dictionary<string, OhlcvBar[]>> LoadReferenceDataAsync(
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
                    DateTime.UtcNow.AddDays(-400), DateTime.UtcNow, ct))
                .OrderBy(bar => bar.Timestamp)
                .ToArray();
        }
        return result;
    }

    /// <summary>
    /// 청산 주문 직후 브로커 주문 내역을 조회하여 실제 체결가(AverageFillPrice)를 가져온다.
    /// 시장가 주문은 수초 내에 체결되므로 최대 5초 대기 후 조회한다.
    /// 조회 실패 또는 fill price 없음 → 0 반환 (호출자가 폴백 처리).
    /// </summary>
    private async Task<decimal> TryGetFillPriceAsync(
        IBrokerService brokerService,
        string symbol,
        DateTime orderSubmittedAt,
        CancellationToken ct)
    {
        try
        {
            // 시장가 청산 주문이 체결될 때까지 최대 5초 대기 (500ms 간격으로 최대 10회 폴링)
            const int maxAttempts = 10;
            const int delayMs = 500;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(delayMs, ct);

                var from = orderSubmittedAt.AddSeconds(-2); // 약간의 클럭 여유
                var to = DateTime.UtcNow.AddSeconds(1);

                var orders = await brokerService.GetOrderHistoryAsync(from, to, ct);

                var fillPrice = orders
                    .Where(o => string.Equals(o.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                                && o.Status == BrokerOrderStatus.Filled
                                && o.AverageFillPrice.HasValue)
                    .OrderByDescending(o => o.FilledAt ?? o.SubmittedAt)
                    .Select(o => o.AverageFillPrice!.Value)
                    .FirstOrDefault();

                if (fillPrice > 0)
                {
                    _logger.LogDebug(
                        "[EXIT-MGR] {Symbol}: 브로커 체결가 조회 성공 — {FillPrice:F4} (시도: {Attempt}/{Max})",
                        symbol, fillPrice, attempt + 1, maxAttempts);
                    return fillPrice;
                }
            }

            _logger.LogDebug(
                "[EXIT-MGR] {Symbol}: 체결가 조회 실패 — 마지막 폴링 가격으로 폴백",
                symbol);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[EXIT-MGR] {Symbol}: 체결가 조회 중 오류 — 폴링 가격으로 폴백", symbol);
            return 0;
        }
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

    private sealed class ExitState
    {
        public bool TrailingActivated { get; set; }
        public bool BreakevenApplied { get; set; }
    }
}
