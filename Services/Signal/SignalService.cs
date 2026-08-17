using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Risk;
using StockTrader.Services.Statistics;

namespace StockTrader.Services.Signal;

public class SignalService : ISignalService
{
    private readonly IStatisticsService _statsService;
    private readonly IRiskManagementService _riskService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ICompiledStrategyRepository _strategies;
    private readonly AppDbContext _db;
    private readonly TradingSettings _tradingSettings;
    private readonly ILogger<SignalService> _logger;

    public SignalService(
        IStatisticsService statsService,
        IRiskManagementService riskService,
        ISettingsRepository settingsRepo,
        ICompiledStrategyRepository strategies,
        AppDbContext db,
        IOptions<TradingSettings> tradingSettings,
        ILogger<SignalService> logger)
    {
        _statsService = statsService;
        _riskService = riskService;
        _settingsRepo = settingsRepo;
        _strategies = strategies;
        _db = db;
        _tradingSettings = tradingSettings.Value;
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

        var customNames = signals
            .Select(signal => signal.CustomPatternName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var customStrategies = await _strategies.GetByNamesAsync(customNames, ct);
        var customTradeHistory = customNames.Count == 0
            ? []
            : await _db.TradeRecords.AsNoTracking()
                .Where(trade => trade.CustomPatternName != null && customNames.Contains(trade.CustomPatternName))
                .OrderBy(trade => trade.ExitTime)
                .ToListAsync(ct);
        var openCustomPositions = customNames.Count == 0
            ? []
            : await _db.Positions.AsNoTracking()
                .Where(position => position.ClosedAt == null)
                .ToListAsync(ct);
        var todayUtc = DateTime.UtcNow.Date;
        var executedToday = customNames.Count == 0
            ? []
            : await _db.TradeRecommendations.AsNoTracking()
                .Where(rec => rec.WasExecuted && rec.GeneratedAt >= todayUtc && rec.CustomPatternName != null)
                .ToListAsync(ct);

        // 섹터 정보를 일괄 조회하여 N+1 방지 (W02 fix)
        var symbols = signals.Select(s => s.Symbol).Distinct().ToList();
        var sectorMap = await _db.Tickers
            .Where(t => symbols.Contains(t.Symbol))
            .ToDictionaryAsync(t => t.Symbol, t => t.Sector, StringComparer.OrdinalIgnoreCase, ct);

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
            var strategyTrades = string.IsNullOrWhiteSpace(signal.CustomPatternName)
                ? []
                : customTradeHistory.Where(trade => string.Equals(
                    trade.CustomPatternName, signal.CustomPatternName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (customDefinition != null && (!customDefinition.IsActive || !customDefinition.EnableLiveTrading))
                continue;
            if (portfolioRules?.MaxTotalPositions > 0
                && openCustomPositions.Count + recommendations.Count >= portfolioRules.MaxTotalPositions)
            {
                _logger.LogInformation("Custom strategy {Strategy} blocked: maximum open positions reached", signal.CustomPatternName);
                continue;
            }
            if (portfolioRules?.MaxEntriesPerDay > 0
                && executedToday.Count(rec => string.Equals(rec.CustomPatternName, signal.CustomPatternName, StringComparison.OrdinalIgnoreCase))
                    + recommendations.Count(rec => string.Equals(rec.CustomPatternName, signal.CustomPatternName, StringComparison.OrdinalIgnoreCase))
                    >= portfolioRules.MaxEntriesPerDay)
            {
                _logger.LogInformation("Custom strategy {Strategy} blocked: daily entry limit reached", signal.CustomPatternName);
                continue;
            }
            if (IsInLiveCooldown(strategyTrades, reentryRules, breakerRules, out var cooldownReason))
            {
                _logger.LogInformation("Custom strategy {Strategy} blocked: {Reason}", signal.CustomPatternName, cooldownReason);
                continue;
            }
            if (breakerRules?.MaxDrawdownPercent > 0
                && ComputeStrategyDrawdown(strategyTrades, settings.AccountSize) >= breakerRules.MaxDrawdownPercent)
            {
                _logger.LogWarning("Custom strategy {Strategy} blocked: drawdown circuit breaker", signal.CustomPatternName);
                continue;
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
            var sector = sectorMap.TryGetValue(signal.Symbol, out var s) && !string.IsNullOrEmpty(s)
                ? s
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
                .Select(trade => new PositionSizingTradeSample(trade.PnL, trade.PnLPercent))
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
                Symbol = signal.Symbol,
                PatternType = signal.PatternType,
                CustomPatternName = signal.CustomPatternName,
                GeneratedAt = DateTime.UtcNow,
                EntryPrice = signal.EntryPrice,
                StopLossPrice = signal.StopLossPrice,
                TargetPrice = signal.TargetPrice,
                PositionSize = positionSize,
                ShareQuantity = shareQty,
                Expectancy = stats?.Expectancy ?? 0m,
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

    private static bool IsInLiveCooldown(
        List<TradeRecord> trades,
        ReentryConfig? reentry,
        CircuitBreakerConfig? breaker,
        out string reason)
    {
        reason = string.Empty;
        if (trades.Count == 0) return false;
        var latest = trades[^1];
        var waitDays = latest.PnL < 0 ? reentry?.CooldownBarsAfterLoss ?? 0 : reentry?.CooldownBarsAfterWin ?? 0;
        if (waitDays > 0 && DateTime.UtcNow.Date < AddTradingDays(latest.ExitTime.Date, waitDays))
        {
            reason = $"재매수 대기 {waitDays}봉";
            return true;
        }

        if (breaker?.ConsecutiveLossLimit > 0)
        {
            var consecutiveLosses = trades.AsEnumerable().Reverse().TakeWhile(trade => trade.PnL < 0).Count();
            if (consecutiveLosses >= breaker.ConsecutiveLossLimit
                && DateTime.UtcNow.Date < AddTradingDays(latest.ExitTime.Date, breaker.CooldownBars))
            {
                reason = $"연속 손실 후 {breaker.CooldownBars}봉 중단";
                return true;
            }
        }
        return false;
    }

    private static DateTime AddTradingDays(DateTime date, int tradingDays)
    {
        var result = date.Date;
        var remaining = Math.Max(0, tradingDays);
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            remaining--;
        }
        return result;
    }

    private static decimal ComputeStrategyDrawdown(List<TradeRecord> trades, decimal accountSize)
    {
        decimal equity = Math.Max(1m, accountSize), peak = equity, maximum = 0m;
        foreach (var trade in trades.OrderBy(trade => trade.ExitTime))
        {
            equity += trade.PnL;
            peak = Math.Max(peak, equity);
            if (peak > 0) maximum = Math.Max(maximum, (peak - equity) / peak * 100m);
        }
        return maximum;
    }
}
