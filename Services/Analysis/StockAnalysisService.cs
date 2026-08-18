using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StockTrader.Application.Analysis;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Patterns;
using StockTrader.Services.Statistics;

namespace StockTrader.Services.Analysis;

public class StockAnalysisService : IStockAnalysisService
{
    private readonly IDataFeedServiceFactory _dataFeedFactory;
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly IStatisticsService _statisticsService;
    private readonly ITradeRepository _tradeRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly StockIndicatorSnapshotFactory _indicatorSnapshots;
    private readonly StockAnalysisSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StockAnalysisService> _logger;

    public StockAnalysisService(
        IDataFeedServiceFactory dataFeedFactory,
        IEnumerable<IPatternDetector> detectors,
        IStatisticsService statisticsService,
        ITradeRepository tradeRepo,
        ISettingsRepository settingsRepo,
        IOhlcvRepository ohlcvRepo,
        StockIndicatorSnapshotFactory indicatorSnapshots,
        IOptions<StockAnalysisSettings> settings,
        TimeProvider timeProvider,
        IMemoryCache cache,
        ILogger<StockAnalysisService> logger)
    {
        _dataFeedFactory = dataFeedFactory;
        _detectors = detectors;
        _statisticsService = statisticsService;
        _tradeRepo = tradeRepo;
        _settingsRepo = settingsRepo;
        _ohlcvRepo = ohlcvRepo;
        _indicatorSnapshots = indicatorSnapshots;
        _settings = settings.Value;
        _timeProvider = timeProvider;
        _cache = cache;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // 관심종목 전체 분석 (병렬, 캐싱, 공급자 기준 레짐 공유)
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

        var feedSelection = await _dataFeedFactory.SelectAsync(null, ct);
        var dataFeed = feedSelection.Service;
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);

        // 1. 기준 종목 레짐과 패턴 통계를 한 번만 가져옴 (모든 종목이 공유)
        var (regime, allStats) = await FetchSharedDataAsync(dataFeed, regimeSymbol, ct);

        // 2. SemaphoreSlim으로 동시 분석 수 제한 (Yahoo rate limit 보호)
        var sem = new SemaphoreSlim(_settings.MaxParallelAnalyses, _settings.MaxParallelAnalyses);
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
                    _cache.Set(cacheKey, analysis, TimeSpan.FromSeconds(_settings.AnalysisCacheSeconds));
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
        var feedSelection = await _dataFeedFactory.SelectAsync(null, ct);
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
        return await GetCachedRegimeAsync(feedSelection.Service, regimeSymbol, ct);
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

        var feedSelection = await _dataFeedFactory.SelectAsync(null, ct);
        var dataFeed = feedSelection.Service;
        var regimeSymbol = DataProviderCatalog.RegimeBenchmarkSymbol(feedSelection.Source);
        var (regime, allStats) = await FetchSharedDataAsync(dataFeed, regimeSymbol, ct);
        var settings = await _settingsRepo.GetAsync(ct);

        var analysis = await AnalyzeInternalAsync(symbol, dataFeed, regime, allStats, settings, ct);
        _cache.Set(cacheKey, analysis, TimeSpan.FromSeconds(_settings.AnalysisCacheSeconds));
        return analysis;
    }

    // ═══════════════════════════════════════════════════════════════
    // 공유 데이터 (레짐 + 패턴 통계) - 캐싱으로 중복 호출 제거
    // ═══════════════════════════════════════════════════════════════

    private async Task<(MarketRegime regime, List<PatternStats> allStats)> FetchSharedDataAsync(
        IDataFeedService dataFeed,
        string regimeSymbol,
        CancellationToken ct)
    {
        // 레짐과 통계를 동시에 가져옴
        var regimeTask = GetCachedRegimeAsync(dataFeed, regimeSymbol, ct);
        var statsTask  = GetCachedStatsAsync(ct);
        await Task.WhenAll(regimeTask, statsTask);
        return (await regimeTask, await statsTask);
    }

    private async Task<MarketRegime> GetCachedRegimeAsync(
        IDataFeedService dataFeed,
        string regimeSymbol,
        CancellationToken ct)
    {
        var key = $"market:regime:{regimeSymbol.ToUpperInvariant()}";
        if (_cache.TryGetValue(key, out MarketRegime? cached) && cached != null)
            return cached;

        var regime = await ComputeRegimeAsync(dataFeed, regimeSymbol, ct);
        _cache.Set(key, regime, TimeSpan.FromMinutes(_settings.RegimeCacheMinutes));
        return regime;
    }

    private async Task<List<PatternStats>> GetCachedStatsAsync(CancellationToken ct)
    {
        const string key = "pattern:stats:all";
        if (_cache.TryGetValue(key, out List<PatternStats>? cached) && cached != null)
            return cached;

        var stats = await _statisticsService.GetAllStatsAsync(ct);
        _cache.Set(key, stats, TimeSpan.FromMinutes(_settings.StatisticsCacheMinutes));
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
        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var bars = await dataFeed.GetHistoricalBarsAsync(
            symbol, TimeFrame.Daily,
            observedAt.AddDays(-_settings.HistoryLookbackDays), observedAt, ct);

        if (bars.Count < _settings.MinimumHistoryBars)
        {
            return new StockAnalysis
            {
                Symbol       = symbol,
                CurrentPrice = bars.Count > 0 ? bars[^1].Close : 0,
                AnalyzedAt   = observedAt,
                Grade        = RecommendationGrade.Neutral
            };
        }

        var barsArray = bars.ToArray();
        var currentPrice = barsArray[^1].Close;

        // 2. 기술지표 계산 (CPU-bound, 동기)
        var marketSnapshot = _indicatorSnapshots.Create(barsArray);
        var indicatorSnapshot = marketSnapshot.Indicators;
        var atr = marketSnapshot.Atr;

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

        var recommendation = StockRecommendationPolicy.Evaluate(new StockRecommendationInput(
            currentPrice,
            atr,
            activePatterns,
            indicatorSnapshot,
            marketSnapshot.VolumeRatio,
            allStats));
        var holdingDays = await ComputeExpectedHoldingDaysAsync(activePatterns, ct);

        return new StockAnalysis
        {
            Symbol                = symbol,
            CurrentPrice          = currentPrice,
            AnalyzedAt            = observedAt,
            UpsideProbability     = recommendation.UpsideProbability,
            ExpectedReturnPercent = recommendation.ExpectedReturnPercent,
            ExpectedHoldingDays   = holdingDays,
            DownsideRiskPercent   = recommendation.DownsideRiskPercent,
            RecommendedStopLoss   = recommendation.RecommendedStopLoss,
            RecommendedTarget     = recommendation.RecommendedTarget,
            ConfidenceScore       = recommendation.ConfidenceScore,
            Grade                 = recommendation.Grade,
            ActivePatterns        = activePatterns,
            Indicators            = indicatorSnapshot,
            ATR                   = atr
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
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
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // ═══════════════════════════════════════════════════════════════
    // 마켓 레짐 계산 — 공급자 기준 종목의 DB 데이터를 우선 사용해 외부 API 호출 최소화
    // ═══════════════════════════════════════════════════════════════
    private async Task<MarketRegime> ComputeRegimeAsync(
        IDataFeedService dataFeed,
        string regimeSymbol,
        CancellationToken ct)
    {
        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var regime = new MarketRegime { AsOf = observedAt };

        try
        {
            List<OhlcvBar> regimeBars;

            // DB에서 기준 종목 일봉 데이터를 설정된 레짐 lookback 범위로 우선 조회한다.
            var dbBars = await _ohlcvRepo.GetBarsAsync(
                regimeSymbol, TimeFrame.Daily,
                observedAt.AddDays(-_settings.RegimeLookbackDays), observedAt, ct);

            if (dbBars.Count >= _settings.MinimumRegimeBars)
            {
                // DB에 충분한 데이터가 있으면 외부 API 호출 생략
                regimeBars = dbBars;
                _logger.LogDebug(
                    "[Analysis] {Symbol} 레짐: DB에서 {Count}개 바 사용 (API 호출 생략)",
                    regimeSymbol,
                    dbBars.Count);
            }
            else
            {
                // DB 데이터 부족 시 외부 API에서 fetch
                _logger.LogDebug(
                    "[Analysis] {Symbol} 레짐: DB 데이터 부족({Count}개) — 외부 API 호출",
                    regimeSymbol,
                    dbBars.Count);
                regimeBars = await dataFeed.GetHistoricalBarsAsync(
                    regimeSymbol, TimeFrame.Daily,
                    observedAt.AddDays(-_settings.RegimeLookbackDays), observedAt, ct);
            }

            if (regimeBars.Count >= _settings.MinimumRegimeBars)
            {
                var trend = _indicatorSnapshots.CreateLongTrend(regimeBars);
                regime.SpyPrice      = trend.Price;
                regime.Spy200Ma      = trend.MovingAverage;
                regime.SpyAbove200Ma = trend.IsAboveMovingAverage;
                regime.RegimeLabel   = regime.SpyAbove200Ma ? "강세" : "약세";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Analysis] {Symbol} 레짐 계산 실패", regimeSymbol);
            regime.RegimeLabel = "알 수 없음";
        }

        return regime;
    }
}
