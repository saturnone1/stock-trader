using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.MarketData;
using StockTrader.Application.Risk;
using StockTrader.Application.Signals;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.Statistics;
using StockTrader.Application.Settings;
using StockTrader.Services.TradingCore;

namespace StockTrader.Services.Signal;

public class SignalService : ISignalService
{
    private readonly IStatisticsService _statsService;
    private readonly IRiskManagementService _riskService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ICompiledStrategyRepository _strategies;
    private readonly ILiveSignalEvaluationStore _evaluationStore;
    private readonly TradingSettings _tradingSettings;
    private readonly TimeProvider _timeProvider;
    private readonly IMarketCalendar _marketCalendar;
    private readonly PatternSettings _patternSettings;
    private readonly ILiveParameterService _liveParameters;
    private readonly ILogger<SignalService> _logger;

    public SignalService(
        IStatisticsService statsService,
        IRiskManagementService riskService,
        ISettingsRepository settingsRepo,
        ICompiledStrategyRepository strategies,
        ILiveSignalEvaluationStore evaluationStore,
        IOptions<TradingSettings> tradingSettings,
        TimeProvider timeProvider,
        IMarketCalendar marketCalendar,
        IOptions<PatternSettings> patternSettings,
        ILiveParameterService liveParameters,
        ILogger<SignalService> logger)
    {
        _statsService = statsService;
        _riskService = riskService;
        _settingsRepo = settingsRepo;
        _strategies = strategies;
        _evaluationStore = evaluationStore;
        _tradingSettings = tradingSettings.Value;
        _timeProvider = timeProvider;
        _marketCalendar = marketCalendar;
        _patternSettings = patternSettings.Value;
        _liveParameters = liveParameters;
        _logger = logger;
    }

    /// <summary>
    /// 거래 이력이 부족한 패턴의 최소 샘플 수.
    /// 이 수 미만이면 Expectancy 필터를 우회하여 신규 패턴도 거래 가능.
    /// </summary>
    private const int MinSamplesForExpectancyFilter = 10;

    public async Task<List<TradeRecommendation>> EvaluateSignalsAsync(
        List<PatternSignal> signals, CancellationToken ct = default)
    {
        var recommendations = new List<TradeRecommendation>();
        var settings = await _settingsRepo.GetAsync(ct);
        var liveOverrides = (await _liveParameters.GetAsync(ct)).Overrides;

        var customNames = signals
            .Select(signal => signal.CustomPatternName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var customStrategies = await _strategies.GetByNamesAsync(customNames, ct);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var marketTimeZone = _marketCalendar.GetTimeZone(MarketRegion.UnitedStates);
        var marketDate = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, marketTimeZone).Date;
        var marketSessionStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(marketDate, DateTimeKind.Unspecified), marketTimeZone);
        var symbols = signals.Select(s => s.Symbol).Distinct().ToList();
        var evaluation = await _evaluationStore.LoadAsync(
            customNames,
            symbols,
            marketSessionStartUtc,
            ct);

        // 패턴 통계를 루프 진입 전 일괄 로드하여 N+1 DB 왕복 제거.
        // GetAllStatsAsync는 단일 쿼리로 전체 PatternStats를 반환한다.
        var allStats = await _statsService.GetAllStatsAsync(ct);
        var statsCache = (allStats ?? []).ToDictionary(s => s.PatternType);

        var recommendedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signal in signals.OrderByDescending(signal => signal.Confidence))
        {
            if (recommendedSymbols.Contains(signal.Symbol))
            {
                _logger.LogInformation("Signal {Strategy} for {Symbol} skipped: a higher-confidence strategy already selected", signal.CustomPatternName ?? signal.PatternType.ToString(), signal.Symbol);
                continue;
            }
            customStrategies.TryGetValue(signal.CustomPatternName ?? string.Empty, out var customStrategy);
            var isCustomSignal = !string.IsNullOrWhiteSpace(signal.CustomPatternName);
            if (isCustomSignal && customStrategy is null)
            {
                _logger.LogWarning("Custom strategy signal {Strategy} skipped: no valid compiled strategy exists", signal.CustomPatternName);
                continue;
            }
            var customDefinition = customStrategy?.Source;
            var portfolioRules = customStrategy?.PortfolioRules;
            var reentryRules = customStrategy?.Reentry;
            var breakerRules = customStrategy?.CircuitBreaker;
            var strategyTrades = evaluation.CompletedTradesFor(signal.CustomPatternName);

            if (customDefinition != null && (!customDefinition.IsActive || !customDefinition.EnableLiveTrading))
                continue;
            if (customStrategy is not null)
            {
                var cooldowns = StrategyHistoricalCooldownPolicy.Evaluate(
                    strategyTrades,
                    reentryRules ?? new ReentryConfig(),
                    breakerRules ?? new CircuitBreakerConfig(),
                    marketDate,
                    _marketCalendar.TradingDayPredicate(MarketRegion.UnitedStates));
                var entriesToday = evaluation.ExecutedEntriesFor(signal.CustomPatternName)
                    + recommendations.Count(rec => string.Equals(
                        rec.CustomPatternName, signal.CustomPatternName, StringComparison.OrdinalIgnoreCase));
                var drawdownBlocked = breakerRules?.MaxDrawdownPercent > 0
                    && StrategyDrawdownPolicy.EvaluateHistory(
                        settings.AccountSize,
                        strategyTrades.OrderBy(trade => trade.ExitedAt).Select(trade => trade.RealizedPnl),
                        breakerRules.MaxDrawdownPercent).IsBlocked;
                var entryEligibility = StrategyEntryEligibilityPolicy.Evaluate(
                    new StrategyEntryEligibilityRequest(
                        _tradingSettings.MaxTotalPositions,
                        portfolioRules?.MaxTotalPositions ?? 0,
                        evaluation.OpenPositionCount + recommendations.Count,
                        drawdownBlocked,
                        cooldowns.ConsecutiveLossBlocked,
                        portfolioRules?.MaxEntriesPerDay ?? 0,
                        entriesToday,
                        cooldowns.ReentryBlocked));
                if (!entryEligibility.CanEnter)
                {
                    _logger.LogInformation(
                        "Custom strategy {Strategy} blocked: {Reason}",
                        signal.CustomPatternName,
                        DescribeEntryBlock(entryEligibility.BlockReason));
                    continue;
                }
            }
            // ── 1. 신뢰도 필터: 자동매매 실행 최소 기준 ──
            if (signal.Confidence < _tradingSettings.MinConfidence)
            {
                _logger.LogDebug(
                    "Signal {Pattern} for {Symbol} filtered: confidence {Actual:F2} < min {Min:F2}",
                    signal.PatternType, signal.Symbol, signal.Confidence, _tradingSettings.MinConfidence);
                continue;
            }

            // ── 2. 가격 유효성: 손절 < 진입 < 목표가 (위반 시 주문 불가) ──
            if (signal.StopLossPrice >= signal.EntryPrice || signal.TargetPrice <= signal.EntryPrice)
            {
                _logger.LogDebug(
                    "Signal {Pattern} for {Symbol} filtered: invalid prices (SL={SL:F2}, Entry={Entry:F2}, Target={Target:F2})",
                    signal.PatternType, signal.Symbol,
                    signal.StopLossPrice, signal.EntryPrice, signal.TargetPrice);
                continue;
            }

            // ── 3. 기대값 필터 ──
            PatternStats? stats = null;
            if (string.IsNullOrWhiteSpace(signal.CustomPatternName))
                statsCache.TryGetValue(signal.PatternType, out stats);

            // Gap 3 fix: 거래 이력이 충분한 경우에만 Expectancy 필터 적용.
            // 신규 패턴(stats==null 또는 샘플 부족)은 통과시켜서 거래 기회 확보.
            if (stats != null
                && stats.SampleSize >= MinSamplesForExpectancyFilter
                && stats.Expectancy <= _tradingSettings.MinExpectancy)
            {
                _logger.LogDebug(
                    "Signal {Pattern} for {Symbol} filtered: expectancy {Actual:F4} <= min {Min:F4} (samples={Samples})",
                    signal.PatternType, signal.Symbol,
                    stats.Expectancy, _tradingSettings.MinExpectancy, stats.SampleSize);
                continue;
            }

            // ── 4. 리스크 체크 ──
            // W02 fix: Tickers 테이블에서 섹터 조회; 없으면 심볼 자체를 섹터로 사용하여
            // 동일 종목 중복 포지션을 MaxPositionsPerSector 체크가 잡아낼 수 있도록 함.
            var storedSector = evaluation.SectorFor(signal.Symbol);
            var sector = !string.IsNullOrEmpty(storedSector)
                ? storedSector
                : signal.Symbol;

            var (allowed, reason) = await _riskService.CanOpenPositionAsync(
                signal.Symbol, sector, ct);

            if (!allowed)
            {
                _logger.LogInformation("Signal {Pattern} for {Symbol} blocked by risk: {Reason}",
                    signal.PatternType, signal.Symbol, reason);
                continue;
            }

            // ── 5. 포지션 사이징 ──
            var sizingTrades = strategyTrades
                .Select(trade => new PositionSizingTradeSample(
                    trade.RealizedPnl,
                    trade.ReturnFraction))
                .ToArray();
            var effectiveRisk = LongPositionSizingPolicy.ResolveRiskFraction(
                _tradingSettings.RiskPerTradePercent,
                customDefinition?.SizingMode,
                sizingTrades);
            var positionSize = _riskService.CalculatePositionSize(
                settings.AccountSize,
                effectiveRisk,
                signal.EntryPrice,
                signal.StopLossPrice);
            positionSize *= PositionAllocationPolicy.NormalizeScale(signal.AllocationScale);

            positionSize = LongPositionSizingPolicy.ApplyPositionCapitalCap(
                positionSize,
                settings.AccountSize,
                _tradingSettings.MaxTotalPositions,
                portfolioRules?.MaxSinglePositionPercent ?? 0m);
            var shareQty = LongPositionSizingPolicy.CalculateAffordableQuantity(
                positionSize, signal.EntryPrice);

            // ── 6. 주문 수량 검증: 0주면 자동매매 실행 불가 ──
            if (shareQty <= 0)
            {
                _logger.LogDebug(
                    "Signal {Pattern} for {Symbol} filtered: calculated share qty = 0 (posSize={PosSize:F2}, entry={Entry:F2})",
                    signal.PatternType, signal.Symbol, positionSize, signal.EntryPrice);
                continue;
            }

            var recommendation = new TradeRecommendation
            {
                SourceSignalId = signal.Id > 0 ? signal.Id : null,
                Symbol = signal.Symbol,
                PatternType = signal.PatternType,
                CustomPatternName = signal.CustomPatternName,
                GeneratedAt = nowUtc,
                EntryPrice = signal.EntryPrice,
                StopLossPrice = signal.StopLossPrice,
                TargetPrice = signal.TargetPrice,
                PositionSize = positionSize,
                ShareQuantity = shareQty,
                Expectancy = stats?.Expectancy ?? 0m,
                ExecutionSector = sector,
                ExecutionArtifact = TradingExecutionArtifactFactory.Create(
                    signal, customStrategy, _patternSettings, liveOverrides),
                WasExecuted = false,
                Mode = settings.OrderMode
            };

            recommendations.Add(recommendation);
            recommendedSymbols.Add(signal.Symbol);
            _logger.LogInformation(
                "Recommendation: {Pattern} {Symbol} Entry={Entry} SL={SL} Target={Target} Qty={Qty}",
                signal.PatternType, signal.Symbol, signal.EntryPrice,
                signal.StopLossPrice, signal.TargetPrice, shareQty);
        }

        _logger.LogInformation(
            "Signal evaluation complete: {Input} signals → {Output} recommendations (filtered: {Filtered})",
            signals.Count, recommendations.Count, signals.Count - recommendations.Count);

        return recommendations;
    }

    private static string DescribeEntryBlock(StrategyEntryBlockReason reason) => reason switch
    {
        StrategyEntryBlockReason.PositionLimit => "보유 한도 도달",
        StrategyEntryBlockReason.DrawdownCircuitBreaker => "최대 낙폭 중단",
        StrategyEntryBlockReason.ConsecutiveLossCircuitBreaker => "연속 손실 후 거래 중단",
        StrategyEntryBlockReason.SessionEntryLimit => "하루 매수 횟수 도달",
        StrategyEntryBlockReason.ReentryCooldown => "재매수 대기",
        _ => "진입 제한"
    };

}
