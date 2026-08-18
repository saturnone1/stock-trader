using Microsoft.Extensions.Caching.Memory;
using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.Statistics;

public class StatisticsService : IStatisticsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string AllStatsCacheKey = "PatternStats_All";

    private readonly IPatternStatsRepository _statsRepo;
    private readonly ITradeHistoryStore _tradeHistory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StatisticsService> _logger;

    public StatisticsService(IPatternStatsRepository statsRepo,
        ITradeHistoryStore tradeHistory, IMemoryCache cache, ILogger<StatisticsService> logger)
    {
        _statsRepo = statsRepo;
        _tradeHistory = tradeHistory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PatternStats?> GetStatsAsync(PatternType pattern, string? symbol = null,
        CancellationToken ct = default)
    {
        var cacheKey = $"PatternStats_{pattern}_{symbol ?? "ALL"}";
        if (_cache.TryGetValue(cacheKey, out PatternStats? cached))
            return cached;

        var stats = await _statsRepo.GetAsync(pattern, symbol, ct);
        if (stats != null)
            _cache.Set(cacheKey, stats, CacheDuration);
        return stats;
    }

    public async Task<List<PatternStats>> GetAllStatsAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(AllStatsCacheKey, out List<PatternStats>? cached))
            return cached!;

        var stats = await _statsRepo.GetAllAsync(ct);
        _cache.Set(AllStatsCacheKey, stats, CacheDuration);
        return stats;
    }

    public Task<PatternStats> ComputeStatsAsync(PatternType pattern, List<TradeRecord> trades,
        CancellationToken ct = default)
    {
        var patternTrades = trades.Where(t => t.PatternType == pattern).ToList();
        var stats = new PatternStats
        {
            PatternType = pattern,
            SampleSize = patternTrades.Count,
            LastUpdated = DateTime.UtcNow
        };

        if (patternTrades.Count == 0)
            return Task.FromResult(stats);

        var wins = patternTrades.Where(t => t.IsWin).ToList();
        var losses = patternTrades.Where(t => !t.IsWin).ToList();

        stats.WinRate = (decimal)wins.Count / patternTrades.Count;
        stats.AvgWinPercent = wins.Count > 0 ? wins.Average(t => t.PnLPercent) : 0;
        stats.AvgLossPercent = losses.Count > 0 ? Math.Abs(losses.Average(t => t.PnLPercent)) : 0;

        // Max drawdown calculation
        decimal peak = 0, maxDd = 0, cumPnl = 0;
        foreach (var trade in patternTrades.OrderBy(t => t.EntryTime))
        {
            cumPnl += trade.PnLPercent;
            if (cumPnl > peak) peak = cumPnl;
            var dd = peak - cumPnl;
            if (dd > maxDd) maxDd = dd;
        }
        stats.MaxDrawdownPercent = maxDd;

        return Task.FromResult(stats);
    }

    public async Task RefreshAllStatsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Refreshing all pattern stats...");
        // 전체 거래 이력이 필요: int.MaxValue로 기본 1000건 상한 우회
        var allTrades = await _tradeHistory.GetTradesAsync(take: int.MaxValue, ct: ct);

        // 거래 이력이 전혀 없으면 기존 통계를 보존하고 종료한다.
        // 이력이 없는 상태에서 계속 진행하면 activeKeys가 비어 있어 DeleteStaleAsync가
        // 기존 PatternStats 행 전체를 삭제하는 버그가 발생한다.
        if (allTrades.Count == 0)
        {
            _logger.LogInformation("No trade records found — skipping stats refresh to preserve existing stats.");
            return;
        }

        // 종목별 캐시 키 수집 (갱신 후 무효화용)
        var symbols = allTrades.Select(t => t.Symbol).Distinct().ToList();

        // 모든 패턴 통계를 메모리에서 먼저 계산한 뒤 단일 SaveChangesAsync로 일괄 저장
        // 기존: 16회 패턴 × 2쿼리(FindAsync + SaveChangesAsync) = 32 queries
        // 개선: 1회 SELECT(전체 로드) + 1회 SaveChangesAsync = 2 queries
        // 이번 계산에서 실제로 통계가 존재하는 (PatternType, Symbol) 조합을 추적
        var activeKeys = new HashSet<(PatternType, string?)>();
        var allStats = new List<PatternStats>();

        foreach (PatternType pattern in Enum.GetValues<PatternType>())
        {
            var stats = await ComputeStatsAsync(pattern, allTrades, ct);
            allStats.Add(stats);
            // SampleSize > 0인 경우만 활성 키로 기록 (거래가 없는 패턴은 stale 대상)
            if (stats.SampleSize > 0)
                activeKeys.Add((pattern, stats.Symbol));
        }

        await _statsRepo.SaveBatchAsync(allStats, ct);

        // 현재 거래 이력에 없는 stale 통계 행 삭제 (예: 데이터 초기화 후 남은 이전 통계)
        await _statsRepo.DeleteStaleAsync(activeKeys, ct);

        // 전체 + 패턴별 + 패턴×종목별 캐시 무효화
        _cache.Remove(AllStatsCacheKey);
        foreach (PatternType pattern in Enum.GetValues<PatternType>())
        {
            _cache.Remove($"PatternStats_{pattern}_ALL");
            foreach (var symbol in symbols)
                _cache.Remove($"PatternStats_{pattern}_{symbol}");
        }

        _logger.LogInformation("Pattern stats refresh complete (cache invalidated)");
    }
}
