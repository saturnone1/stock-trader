using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;
using StockTrader.Services.Statistics;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Analysis;

public class StockAnalysisService : IStockAnalysisService
{
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly IIndicatorService _indicators;
    private readonly IStatisticsService _statisticsService;
    private readonly ITradeRepository _tradeRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly TradingSettings _tradingSettings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StockAnalysisService> _logger;

    // 캐시 TTL 상수
    private static readonly TimeSpan AnalysisCacheTtl = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan RegimeCacheTtl   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StatsCacheTtl    = TimeSpan.FromMinutes(10);

    // 병렬 분석 최대 동시 종목 수 (Yahoo rate limit 고려)
    private const int MaxParallelAnalyses = 3;

    public StockAnalysisService(
        IDataFeedServiceFactory dataFeedFactory,
        IEnumerable<IPatternDetector> detectors,
        IIndicatorService indicators,
        IStatisticsService statisticsService,
        ITradeRepository tradeRepo,
        ISettingsRepository settingsRepo,
        IOhlcvRepository ohlcvRepo,
        IOptions<TradingSettings> tradingSettings,
        IMemoryCache cache,
        ILogger<StockAnalysisService> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _detectors = detectors;
        _indicators = indicators;
        _statisticsService = statisticsService;
        _tradeRepo = tradeRepo;
        _settingsRepo = settingsRepo;
        _ohlcvRepo = ohlcvRepo;
        _tradingSettings = tradingSettings.Value;
        _cache = cache;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // 관심종목 전체 분석 (병렬, 캐싱, SPY 레짐 공유)
    // ═══════════════════════════════════════════════════════════════

    public async Task<List<StockAnalysis>> AnalyzeWatchlistAsync(CancellationToken ct = default)
    {
        var results = new List<StockAnalysis>();
        await AnalyzeWatchlistProgressiveAsync(
            item =>
            {
                lock (results) results.Add(item);
                return Task.CompletedTask;
            },
            ct);

        return results.OrderByDescending(a => a.UpsideProbability).ToList();
    }

    public async Task<List<StockAnalysis>> AnalyzeWatchlistProgressiveAsync(
        Func<StockAnalysis, Task> onItemCompleted,
        CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        var symbols  = settings.WatchlistSymbols;
        if (symbols.Count == 0) return [];

        var dataFeed = await _dataFeedFactory.GetServiceAsync(ct);

        // 1. SPY 레짐과 패턴 통계를 한 번만 가져옴 (모든 종목이 공유)
        var (regime, allStats) = await FetchSharedDataAsync(dataFeed, ct);

        // 2. SemaphoreSlim으로 동시 분석 수 제한 (Yahoo rate limit 보호)
        var sem     = new SemaphoreSlim(MaxParallelAnalyses, MaxParallelAnalyses);
        var results = new System.Collections.Concurrent.ConcurrentBag<StockAnalysis>();

        var tasks = symbols.Select(async symbol =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var cacheKey = $"analysis:{symbol}";
                if (_cache.TryGetValue(cacheKey, out StockAnalysis? cached) && cached != null)
                {
                    _logger.LogDebug("[Analysis] {Symbol} 캐시 히트", symbol);
                    results.Add(cached);
                    await onItemCompleted(cached);
                    return;
                }

                try
                {
                    var analysis = await AnalyzeInternalAsync(symbol, dataFeed, regime, allStats, settings, ct);
                    _cache.Set(cacheKey, analysis, AnalysisCacheTtl);
                    results.Add(analysis);
                    await onItemCompleted(analysis);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Analysis] {Symbol} 분석 실패", symbol);
                }
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);

        return results.OrderByDescending(a => a.UpsideProbability).ToList();
    }

    // ═══════════════════════════════════════════════════════════════
    // 시장 레짐 공개 API (IStockAnalysisService 구현)
    // ═══════════════════════════════════════════════════════════════

    public async Task<MarketRegime> GetMarketRegimeAsync(CancellationToken ct = default)
    {
        var dataFeed = await _dataFeedFactory.GetServiceAsync(ct);
        return await GetCachedRegimeAsync(dataFeed, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // 단일 종목 분석 (공개 API)
    // ═══════════════════════════════════════════════════════════════

    public async Task<StockAnalysis> AnalyzeAsync(string symbol, CancellationToken ct = default)
    {
        var cacheKey = $"analysis:{symbol}";
        if (_cache.TryGetValue(cacheKey, out StockAnalysis? cached) && cached != null)
        {
            _logger.LogDebug("[Analysis] {Symbol} 캐시 히트 (단건)", symbol);
            return cached;
        }

        var dataFeed = await _dataFeedFactory.GetServiceAsync(ct);
        var (regime, allStats) = await FetchSharedDataAsync(dataFeed, ct);
        var settings = await _settingsRepo.GetAsync(ct);

        var analysis = await AnalyzeInternalAsync(symbol, dataFeed, regime, allStats, settings, ct);
        _cache.Set(cacheKey, analysis, AnalysisCacheTtl);
        return analysis;
    }

    // ═══════════════════════════════════════════════════════════════
    // 공유 데이터 (레짐 + 패턴 통계) - 캐싱으로 중복 호출 제거
    // ═══════════════════════════════════════════════════════════════

    private async Task<(MarketRegime regime, List<PatternStats> allStats)> FetchSharedDataAsync(
        IDataFeedService dataFeed, CancellationToken ct)
    {
        // 레짐과 통계를 동시에 가져옴
        var regimeTask = GetCachedRegimeAsync(dataFeed, ct);
        var statsTask  = GetCachedStatsAsync(ct);
        await Task.WhenAll(regimeTask, statsTask);
        return (await regimeTask, await statsTask);
    }

    private async Task<MarketRegime> GetCachedRegimeAsync(IDataFeedService dataFeed, CancellationToken ct)
    {
        const string key = "market:regime";
        if (_cache.TryGetValue(key, out MarketRegime? cached) && cached != null)
            return cached;

        var regime = await ComputeRegimeAsync(dataFeed, ct);
        _cache.Set(key, regime, RegimeCacheTtl);
        return regime;
    }

    private async Task<List<PatternStats>> GetCachedStatsAsync(CancellationToken ct)
    {
        const string key = "pattern:stats:all";
        if (_cache.TryGetValue(key, out List<PatternStats>? cached) && cached != null)
            return cached;

        var stats = await _statisticsService.GetAllStatsAsync(ct);
        _cache.Set(key, stats, StatsCacheTtl);
        return stats;
    }

    // ═══════════════════════════════════════════════════════════════
    // 단일 종목 내부 분석 (공유 데이터를 파라미터로 받음)
    // ═══════════════════════════════════════════════════════════════

    private async Task<StockAnalysis> AnalyzeInternalAsync(
        string symbol,
        IDataFeedService dataFeed,
        MarketRegime regime,
        List<PatternStats> allStats,
        UserSettings settings,
        CancellationToken ct)
    {
        // 1. 히스토리 데이터 가져오기 (1년)
        var bars = await dataFeed.GetHistoricalBarsAsync(
            symbol, TimeFrame.Daily,
            DateTime.UtcNow.AddYears(-1), DateTime.UtcNow, ct);

        if (bars.Count < 50)
        {
            return new StockAnalysis
            {
                Symbol       = symbol,
                CurrentPrice = bars.Count > 0 ? bars[^1].Close : 0,
                AnalyzedAt   = DateTime.UtcNow,
                Grade        = RecommendationGrade.Neutral
            };
        }

        var barsArray    = bars.ToArray();
        var closes       = ExtractCloses(barsArray);
        var currentPrice = closes[^1];
        var currentBar   = barsArray[^1];

        // 2. 기술지표 계산 (CPU-bound, 동기)
        var indicatorSnapshot = ComputeIndicators(barsArray, closes, currentPrice);

        // 3. ATR 계산
        var atrValues = _indicators.ATR(barsArray, 14);
        var atr       = atrValues[^1];

        // 4. 패턴 감지 (활성화된 패턴만)
        var activePatterns = new List<PatternSignalInfo>();
        foreach (var detector in _detectors.Where(d => settings.EnabledPatterns.Contains(d.PatternType)))
        {
            try
            {
                var signal = await detector.DetectAsync(symbol, barsArray, regime, ct);
                if (signal != null)
                {
                    var stats = allStats.FirstOrDefault(s => s.PatternType == detector.PatternType);
                    activePatterns.Add(new PatternSignalInfo
                    {
                        PatternType           = detector.PatternType,
                        Confidence            = signal.Confidence,
                        HistoricalWinRate     = stats?.WinRate ?? 0.5m,
                        HistoricalAvgReturn   = stats?.AvgWinPercent ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Analysis] {Symbol} {Pattern} 감지 오류", symbol, detector.PatternType);
            }
        }

        // 5. 거래량 비율 계산
        var avgVolume20  = bars.TakeLast(20).Average(b => (decimal)b.Volume);
        var volumeRatio  = avgVolume20 > 0 ? (decimal)currentBar.Volume / avgVolume20 : 1m;

        // 6. 핵심 수치 계산
        var upsideProbability = ComputeUpsideProbability(activePatterns, indicatorSnapshot, allStats);
        var expectedReturn    = ComputeExpectedReturn(activePatterns, currentPrice, atr);
        var holdingDays       = await ComputeExpectedHoldingDaysAsync(activePatterns, ct);
        var downsideRisk      = ComputeDownsideRisk(activePatterns, allStats);
        var stopLoss          = ComputeRecommendedStopLoss(currentPrice, atr, activePatterns, allStats);
        var target            = ComputeRecommendedTarget(currentPrice, atr, activePatterns);
        var confidence        = ComputeConfidenceScore(activePatterns, indicatorSnapshot, volumeRatio, allStats);
        var grade             = DetermineGrade(upsideProbability, confidence);

        return new StockAnalysis
        {
            Symbol                = symbol,
            CurrentPrice          = currentPrice,
            AnalyzedAt            = DateTime.UtcNow,
            UpsideProbability     = upsideProbability,
            ExpectedReturnPercent = expectedReturn,
            ExpectedHoldingDays   = holdingDays,
            DownsideRiskPercent   = downsideRisk,
            RecommendedStopLoss   = stopLoss,
            RecommendedTarget     = target,
            ConfidenceScore       = confidence,
            Grade                 = grade,
            ActivePatterns        = activePatterns,
            Indicators            = indicatorSnapshot,
            ATR                   = atr
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // A. 상승 확률 - Naive Bayes Combination
    // ═══════════════════════════════════════════════════════════════
    private decimal ComputeUpsideProbability(
        List<PatternSignalInfo> activePatterns,
        IndicatorSnapshot indicators,
        List<PatternStats> allStats)
    {
        if (activePatterns.Count == 0)
        {
            var baseProb = 50m;
            baseProb = ApplyIndicatorAdjustment(baseProb, indicators);
            return Math.Clamp(baseProb, 5m, 95m);
        }

        // Naive Bayes: P(Up|patterns) = prod(P(Up|pi)) normalized
        double likelihoodUp   = 1.0;
        double likelihoodDown = 1.0;
        const double prior    = 0.5;

        foreach (var pattern in activePatterns)
        {
            var winRate = (double)Math.Clamp(pattern.HistoricalWinRate, 0.1m, 0.9m);
            likelihoodUp   *= winRate;
            likelihoodDown *= (1.0 - winRate);
        }

        var posterior = (likelihoodUp * prior) /
                        (likelihoodUp * prior + likelihoodDown * (1.0 - prior));

        var probability = (decimal)(posterior * 100.0);
        probability = ApplyIndicatorAdjustment(probability, indicators);

        return Math.Clamp(Math.Round(probability, 1), 5m, 95m);
    }

    private decimal ApplyIndicatorAdjustment(decimal probability, IndicatorSnapshot indicators)
    {
        if (indicators.RSI > 0 && indicators.RSI < 30)
            probability *= 1.10m;
        else if (indicators.RSI > 70)
            probability *= 0.90m;

        if (indicators.SMA200 > 0 && indicators.SMA20 > indicators.SMA200)
            probability *= 1.05m;

        if (indicators.MACD > indicators.MACDSignal)
            probability *= 1.05m;

        if (indicators.BollingerLower > 0 && indicators.SMA20 > 0)
        {
            var bbWidth = indicators.BollingerUpper - indicators.BollingerLower;
            if (bbWidth > 0)
            {
                var bbPosition = (indicators.SMA20 - indicators.BollingerLower) / bbWidth;
                if (bbPosition < 0.2m) probability *= 1.05m;
                else if (bbPosition > 0.8m) probability *= 0.95m;
            }
        }

        return probability;
    }

    // ═══════════════════════════════════════════════════════════════
    // B. 예상 수익률
    // ═══════════════════════════════════════════════════════════════
    private decimal ComputeExpectedReturn(
        List<PatternSignalInfo> activePatterns,
        decimal currentPrice, decimal atr)
    {
        if (currentPrice == 0) return 0;

        var atrTargetReturn = currentPrice > 0 ? (2.5m * atr) / currentPrice * 100m : 0;

        if (activePatterns.Count == 0)
            return Math.Round(atrTargetReturn * 0.5m, 2);

        var totalWeight     = activePatterns.Sum(p => p.Confidence);
        var patternAvgReturn = totalWeight > 0
            ? activePatterns.Sum(p => p.HistoricalAvgReturn * p.Confidence) / totalWeight
            : 0;

        return Math.Round(patternAvgReturn * 0.6m + atrTargetReturn * 0.4m, 2);
    }

    // ═══════════════════════════════════════════════════════════════
    // C. 예상 보유 기간
    // ═══════════════════════════════════════════════════════════════
    private async Task<int> ComputeExpectedHoldingDaysAsync(
        List<PatternSignalInfo> activePatterns, CancellationToken ct)
    {
        if (activePatterns.Count == 0) return 20;

        // 보유일 통계 계산을 위해 전체 거래 이력 필요
        var trades       = await _tradeRepo.GetTradesAsync(take: int.MaxValue, ct: ct);
        var patternTypes = activePatterns.Select(p => p.PatternType).ToHashSet();

        var relevantTrades = trades
            .Where(t => patternTypes.Contains(t.PatternType) && t.ExitTime > t.EntryTime)
            .ToList();

        if (relevantTrades.Count == 0) return 20;

        var avgDays = relevantTrades.Average(t => (t.ExitTime - t.EntryTime).TotalDays);
        return Math.Max(1, (int)Math.Round(avgDays));
    }

    // ═══════════════════════════════════════════════════════════════
    // D. 손실 위험
    // ═══════════════════════════════════════════════════════════════
    private decimal ComputeDownsideRisk(
        List<PatternSignalInfo> activePatterns, List<PatternStats> allStats)
    {
        if (activePatterns.Count == 0) return 50m;

        var totalWeight     = 0m;
        var weightedLossRate = 0m;
        var weightedAvgLoss  = 0m;

        foreach (var pattern in activePatterns)
        {
            var stats = allStats.FirstOrDefault(s => s.PatternType == pattern.PatternType);
            if (stats == null) continue;

            var weight       = pattern.Confidence;
            weightedLossRate += (1 - stats.WinRate) * weight;
            weightedAvgLoss  += stats.AvgLossPercent * weight;
            totalWeight      += weight;
        }

        if (totalWeight == 0) return 50m;

        var lossRate = weightedLossRate / totalWeight;
        var avgLoss  = weightedAvgLoss  / totalWeight;

        return Math.Round(lossRate * avgLoss * 100m, 1);
    }

    // ═══════════════════════════════════════════════════════════════
    // E. 추천 손절선
    // ═══════════════════════════════════════════════════════════════
    private decimal ComputeRecommendedStopLoss(
        decimal currentPrice, decimal atr,
        List<PatternSignalInfo> activePatterns, List<PatternStats> allStats)
    {
        var atrStop = currentPrice - 2.0m * atr;

        if (activePatterns.Count > 0)
        {
            var avgDrawdown = activePatterns
                .Select(p => allStats.FirstOrDefault(s => s.PatternType == p.PatternType))
                .Where(s => s != null)
                .Select(s => s!.MaxDrawdownPercent)
                .DefaultIfEmpty(0.05m)
                .Average();

            var maeStop = currentPrice * (1 - avgDrawdown);
            return Math.Round(Math.Max(atrStop, maeStop), 2);
        }

        return Math.Round(atrStop, 2);
    }

    // ═══════════════════════════════════════════════════════════════
    // F. 추천 목표가
    // ═══════════════════════════════════════════════════════════════
    private decimal ComputeRecommendedTarget(
        decimal currentPrice, decimal atr,
        List<PatternSignalInfo> activePatterns)
    {
        var atrTarget = currentPrice + 2.5m * atr;

        if (activePatterns.Count > 0)
        {
            var totalWeight = activePatterns.Sum(p => p.Confidence);
            if (totalWeight > 0)
            {
                var avgReturn     = activePatterns.Sum(p => p.HistoricalAvgReturn * p.Confidence) / totalWeight;
                var patternTarget = currentPrice * (1 + avgReturn / 100m);
                return Math.Round(patternTarget * 0.6m + atrTarget * 0.4m, 2);
            }
        }

        return Math.Round(atrTarget, 2);
    }

    // ═══════════════════════════════════════════════════════════════
    // G. 신뢰도 점수
    // ═══════════════════════════════════════════════════════════════
    private decimal ComputeConfidenceScore(
        List<PatternSignalInfo> activePatterns,
        IndicatorSnapshot indicators,
        decimal volumeRatio,
        List<PatternStats> allStats)
    {
        var patternScore = activePatterns.Count > 0
            ? activePatterns.Average(p => p.HistoricalWinRate)
            : 0m;

        var confluenceScore = indicators.TotalIndicatorCount > 0
            ? (decimal)indicators.BullishIndicatorCount / indicators.TotalIndicatorCount
            : 0.5m;

        var volumeScore = Math.Min(1m, volumeRatio / 1.5m);

        var totalSamples = activePatterns
            .Select(p => allStats.FirstOrDefault(s => s.PatternType == p.PatternType))
            .Where(s => s != null)
            .Sum(s => s!.SampleSize);
        var sampleScore = Math.Min(1m, totalSamples / 50m);

        var total = patternScore    * 0.40m
                  + confluenceScore * 0.30m
                  + volumeScore     * 0.15m
                  + sampleScore     * 0.15m;

        return Math.Round(total * 100m, 0);
    }

    // ═══════════════════════════════════════════════════════════════
    // H. 추천 등급
    // ═══════════════════════════════════════════════════════════════
    private static RecommendationGrade DetermineGrade(decimal probability, decimal confidence)
    {
        if (probability >= 70 && confidence >= 60) return RecommendationGrade.StrongBuy;
        if (probability >= 60 && confidence >= 45) return RecommendationGrade.Buy;
        if (probability >= 40) return RecommendationGrade.Neutral;
        if (probability >= 25) return RecommendationGrade.Sell;
        return RecommendationGrade.StrongSell;
    }

    // ═══════════════════════════════════════════════════════════════
    // 기술지표 스냅샷 계산 (CPU-bound, 동기)
    // ═══════════════════════════════════════════════════════════════
    private IndicatorSnapshot ComputeIndicators(OhlcvBar[] bars, decimal[] closes, decimal currentPrice)
    {
        var snapshot     = new IndicatorSnapshot();
        var bullishCount = 0;
        var totalCount   = 0;

        // RSI
        var rsi = _indicators.RSI(closes, 14);
        snapshot.RSI = rsi.Length > 0 ? rsi[^1] : 50;
        totalCount++;
        if (snapshot.RSI > 30 && snapshot.RSI < 70) bullishCount++;

        // SMA 20
        if (closes.Length >= 20)
        {
            var sma20 = _indicators.SMA(closes, 20);
            snapshot.SMA20 = sma20[^1];
            totalCount++;
            if (currentPrice > snapshot.SMA20) bullishCount++;
        }

        // SMA 50
        if (closes.Length >= 50)
        {
            var sma50 = _indicators.SMA(closes, 50);
            snapshot.SMA50 = sma50[^1];
            totalCount++;
            if (currentPrice > snapshot.SMA50) bullishCount++;
        }

        // SMA 200
        if (closes.Length >= 200)
        {
            var sma200 = _indicators.SMA(closes, 200);
            snapshot.SMA200 = sma200[^1];
            totalCount++;
            if (currentPrice > snapshot.SMA200) bullishCount++;
        }

        // MACD (EMA12 - EMA26, Signal = EMA9 of MACD)
        if (closes.Length >= 26)
        {
            var ema12    = _indicators.EMA(closes, 12);
            var ema26    = _indicators.EMA(closes, 26);
            var macdLine = new decimal[closes.Length];
            for (var i = 0; i < closes.Length; i++)
                macdLine[i] = ema12[i] - ema26[i];

            var signal       = _indicators.EMA(macdLine, 9);
            snapshot.MACD       = macdLine[^1];
            snapshot.MACDSignal = signal[^1];
            totalCount++;
            if (snapshot.MACD > snapshot.MACDSignal) bullishCount++;
        }

        // Bollinger Bands
        if (closes.Length >= 20)
        {
            var bb = _indicators.BollingerBands(closes, 20, 2.0m);
            snapshot.BollingerUpper  = bb.Upper[^1];
            snapshot.BollingerMiddle = bb.Middle[^1];
            snapshot.BollingerLower  = bb.Lower[^1];
            totalCount++;
            if (currentPrice > snapshot.BollingerMiddle) bullishCount++;
        }

        // VWAP
        if (bars.Length > 0)
        {
            var vwap    = _indicators.VWAP(bars);
            snapshot.VWAP = vwap[^1];
            totalCount++;
            if (currentPrice > snapshot.VWAP) bullishCount++;
        }

        snapshot.BullishIndicatorCount = bullishCount;
        snapshot.TotalIndicatorCount   = totalCount;

        return snapshot;
    }

    // ═══════════════════════════════════════════════════════════════
    // 마켓 레짐 계산 (SPY 기반) — DB 우선 조회로 외부 API 호출 최소화
    // ═══════════════════════════════════════════════════════════════
    private async Task<MarketRegime> ComputeRegimeAsync(IDataFeedService dataFeed, CancellationToken ct)
    {
        var regime = new MarketRegime { AsOf = DateTime.UtcNow };

        try
        {
            List<OhlcvBar> spyBars;

            // DB에서 SPY 일봉 데이터 우선 조회 (최근 300일)
            var dbBars = await _ohlcvRepo.GetBarsAsync(
                "SPY", TimeFrame.Daily,
                DateTime.UtcNow.AddDays(-300), DateTime.UtcNow, ct);

            if (dbBars.Count >= 200)
            {
                // DB에 충분한 데이터가 있으면 외부 API 호출 생략
                spyBars = dbBars;
                _logger.LogDebug("[Analysis] SPY 레짐: DB에서 {Count}개 바 사용 (API 호출 생략)", dbBars.Count);
            }
            else
            {
                // DB 데이터 부족 시 외부 API에서 fetch
                _logger.LogDebug("[Analysis] SPY 레짐: DB 데이터 부족({Count}개) — 외부 API 호출", dbBars.Count);
                spyBars = await dataFeed.GetHistoricalBarsAsync(
                    "SPY", TimeFrame.Daily,
                    DateTime.UtcNow.AddDays(-300), DateTime.UtcNow, ct);
            }

            if (spyBars.Count >= 200)
            {
                var spyCloses        = ExtractCloses(spyBars.ToArray());
                var sma200           = _indicators.SMA(spyCloses, 200);
                regime.SpyPrice      = spyCloses[^1];
                regime.Spy200Ma      = sma200[^1];
                regime.SpyAbove200Ma = regime.SpyPrice > regime.Spy200Ma;
                regime.RegimeLabel   = regime.SpyAbove200Ma ? "강세" : "약세";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analysis] SPY 레짐 계산 실패");
            regime.RegimeLabel = "알 수 없음";
        }

        return regime;
    }
}
