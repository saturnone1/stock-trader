using Microsoft.Extensions.Caching.Memory;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.Statistics;

public class StatisticsService : IStatisticsService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private const string AllStatsCacheKey = "PatternStats_All";

    private readonly IPatternStatsRepository _statsRepo;
    private readonly ITradeRepository _tradeRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<StatisticsService> _logger;

    public StatisticsService(IPatternStatsRepository statsRepo,
        ITradeRepository tradeRepo, IMemoryCache cache, ILogger<StatisticsService> logger)
    {
        _statsRepo = statsRepo;
        _tradeRepo = tradeRepo;
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
        var allTrades = await _tradeRepo.GetTradesAsync(ct: ct);

        foreach (PatternType pattern in Enum.GetValues<PatternType>())
        {
            var stats = await ComputeStatsAsync(pattern, allTrades, ct);
            await _statsRepo.SaveAsync(stats, ct);
        }

        // Invalidate cache after refresh
        _cache.Remove(AllStatsCacheKey);
        foreach (PatternType pattern in Enum.GetValues<PatternType>())
            _cache.Remove($"PatternStats_{pattern}_ALL");

        _logger.LogInformation("Pattern stats refresh complete (cache invalidated)");
    }
}
