using StockTrader.Application.Portfolio;
using StockTrader.Application.Trading;
using StockTrader.Data.Repositories;

namespace StockTrader.Services.Portfolio;

public sealed class PortfolioPerformanceQuery(
    ITradeHistoryStore tradeHistory,
    IPatternStatsRepository patternStatistics,
    ISettingsRepository settings)
    : IPortfolioPerformanceQuery
{
    public async Task<PortfolioPerformanceSnapshot> GetAsync(CancellationToken ct = default)
    {
        var tradesTask = tradeHistory.GetTradesAsync(take: int.MaxValue, ct: ct);
        // 두 scoped 저장소는 같은 AppDbContext를 공유하므로 동시에 호출하지 않는다.
        var storedStatistics = await patternStatistics.GetAllAsync(ct);
        var userSettings = await settings.GetAsync(ct);
        var storedTrades = await tradesTask;

        var trades = storedTrades.Select(trade => new PortfolioCompletedTrade(
            trade.Id,
            trade.Symbol,
            trade.PatternType.ToString(),
            trade.ExitTime,
            trade.PnL,
            trade.PnLPercent,
            trade.IsWin));
        var statistics = storedStatistics.Select(stat => new PortfolioPatternStatistics(
            stat.PatternType.ToString(),
            stat.Symbol,
            stat.SampleSize,
            stat.WinRate,
            stat.AvgWinPercent,
            stat.AvgLossPercent,
            stat.MaxDrawdownPercent,
            stat.Expectancy,
            stat.ProfitFactor,
            stat.LastUpdated)).ToArray();
        return PortfolioPerformancePolicy.Evaluate(
            trades,
            userSettings.AccountSize,
            statistics);
    }
}
