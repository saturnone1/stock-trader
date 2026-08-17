using StockTrader.Api.Contracts;
using StockTrader.Data.Repositories;

namespace StockTrader.Api;

public static class PortfolioEndpoints
{
    public static RouteGroupBuilder MapPortfolioApi(this RouteGroupBuilder group)
    {
        // GET /api/portfolio — 보유 포지션 상세
        group.MapGet("/portfolio", async (
            ITradeRepository tradeRepo,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            var positions = await tradeRepo.GetOpenPositionsAsync(ct);

            return Results.Ok(new
            {
                Positions = positions.Select(p => OpenPositionResponseMapper.Map(
                    p, timeProvider.GetUtcNow().UtcDateTime)),
                TotalUnrealizedPnL = positions.Sum(p => p.UnrealizedPnL),
                PositionCount      = positions.Count
            });
        }).RequireAuthorization();

        // GET /api/portfolio/performance — 성과 분석
        group.MapGet("/portfolio/performance", async (
            ITradeRepository tradeRepo,
            IPatternStatsRepository statsRepo,
            CancellationToken ct) =>
        {
            var tradesTask      = tradeRepo.GetTradesAsync(ct: ct);
            var patternStatTask = statsRepo.GetAllAsync(ct);

            await Task.WhenAll(tradesTask, patternStatTask);

            var trades       = await tradesTask;
            var patternStats = await patternStatTask;

            // 성과 계산
            int totalTrades = trades.Count;
            int wins        = trades.Count(t => t.IsWin);
            decimal winRate = totalTrades > 0 ? (decimal)wins / totalTrades : 0m;

            var winTrades  = trades.Where(t => t.IsWin).ToList();
            var lossTrades = trades.Where(t => !t.IsWin).ToList();

            decimal avgWin  = winTrades.Count  > 0 ? winTrades.Average(t => t.PnLPercent)  : 0m;
            decimal avgLoss = lossTrades.Count > 0 ? lossTrades.Average(t => t.PnLPercent) : 0m;

            // 최대 낙폭 계산 (누적 수익 기준)
            decimal maxDrawdown = 0m;
            decimal peak        = 0m;
            decimal cumPnL      = 0m;

            foreach (var trade in trades.OrderBy(t => t.ExitTime))
            {
                cumPnL += trade.PnL;
                if (cumPnL > peak) peak = cumPnL;
                var drawdown = peak > 0 ? (peak - cumPnL) / peak : 0m;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }

            // 수익 곡선 (완료 거래의 누적 수익)
            var equityCurve = new List<object>();
            decimal runningPnL = 0m;
            foreach (var trade in trades.OrderBy(t => t.ExitTime))
            {
                runningPnL += trade.PnL;
                equityCurve.Add(new
                {
                    Date    = trade.ExitTime.ToString("yyyy-MM-dd"),
                    trade.Symbol,
                    Pattern = trade.PatternType.ToString(),
                    trade.PnL,
                    trade.PnLPercent,
                    CumulativePnL = runningPnL
                });
            }

            return Results.Ok(new
            {
                TotalTrades   = totalTrades,
                WinRate       = winRate,
                AvgWinPercent = avgWin,
                AvgLossPercent = avgLoss,
                MaxDrawdown   = maxDrawdown,
                PatternStats  = patternStats.Select(s => new
                {
                    Pattern             = s.PatternType.ToString(),
                    s.Symbol,
                    s.SampleSize,
                    s.WinRate,
                    s.AvgWinPercent,
                    s.AvgLossPercent,
                    s.MaxDrawdownPercent,
                    s.Expectancy,
                    s.ProfitFactor,
                    LastUpdated         = s.LastUpdated.ToString("o")
                }),
                EquityCurve = equityCurve
            });
        }).RequireAuthorization();

        return group;
    }
}
