using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.Statistics;

public class StatisticsService : IStatisticsService
{
    private readonly IPatternStatsRepository _statsRepo;
    private readonly ITradeRepository _tradeRepo;
    private readonly ILogger<StatisticsService> _logger;

    public StatisticsService(IPatternStatsRepository statsRepo,
        ITradeRepository tradeRepo, ILogger<StatisticsService> logger)
    {
        _statsRepo = statsRepo;
        _tradeRepo = tradeRepo;
        _logger = logger;
    }

    public async Task<PatternStats?> GetStatsAsync(PatternType pattern, string? symbol = null,
        CancellationToken ct = default)
    {
        return await _statsRepo.GetAsync(pattern, symbol, ct);
    }

    public async Task<List<PatternStats>> GetAllStatsAsync(CancellationToken ct = default)
    {
        return await _statsRepo.GetAllAsync(ct);
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
        _logger.LogInformation("Pattern stats refresh complete");
    }
}
