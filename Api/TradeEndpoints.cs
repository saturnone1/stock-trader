using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Api;

public static class TradeEndpoints
{
    public static RouteGroupBuilder MapTradeApi(this RouteGroupBuilder group)
    {
        // GET /api/trades/recommendations?count=50
        group.MapGet("/trades/recommendations", async (
            ITradeRepository tradeRepo,
            int? count,
            CancellationToken ct) =>
        {
            var recs = await tradeRepo.GetRecentRecommendationsAsync(count ?? 50, ct);

            return Results.Ok(new
            {
                Count = recs.Count,
                Recommendations = recs.Select(r => new
                {
                    r.Id,
                    r.Symbol,
                    Pattern          = r.PatternType.ToString(),
                    r.EntryPrice,
                    r.StopLossPrice,
                    r.TargetPrice,
                    r.PositionSize,
                    r.ShareQuantity,
                    r.Expectancy,
                    r.RiskRewardRatio,
                    r.StopLossPercent,
                    r.WasExecuted,
                    Mode             = r.Mode.ToString(),
                    GeneratedAt      = r.GeneratedAt.ToString("o")
                })
            });
        }).RequireAuthorization();

        // GET /api/trades/positions — 진행 중 포지션
        group.MapGet("/trades/positions", async (
            ITradeRepository tradeRepo,
            CancellationToken ct) =>
        {
            var positions = await tradeRepo.GetOpenPositionsAsync(ct);

            return Results.Ok(new
            {
                Count = positions.Count,
                Positions = positions.Select(p => new
                {
                    p.Id,
                    p.Symbol,
                    p.Sector,
                    p.Quantity,
                    p.EntryPrice,
                    p.CurrentPrice,
                    p.StopLossPrice,
                    p.TargetPrice,
                    Pattern        = p.PatternType.ToString(),
                    p.UnrealizedPnL,
                    p.AccountId,
                    p.HighSinceEntry,
                    p.EntryAtr,
                    HoldingDays    = (DateTime.UtcNow - p.OpenedAt).Days,
                    OpenedAt       = p.OpenedAt.ToString("o")
                })
            });
        }).RequireAuthorization();

        // GET /api/trades/history?pattern=&from=&to=&skip=0&take=50
        group.MapGet("/trades/history", async (
            ITradeRepository tradeRepo,
            string? pattern,
            string? from,
            string? to,
            int? skip,
            int? take,
            CancellationToken ct) =>
        {
            PatternType? patternFilter = null;
            if (!string.IsNullOrWhiteSpace(pattern) &&
                Enum.TryParse<PatternType>(pattern, ignoreCase: true, out var p))
            {
                patternFilter = p;
            }

            DateTime? fromDate = null;
            if (!string.IsNullOrWhiteSpace(from) &&
                DateTime.TryParse(from, out var fd))
            {
                fromDate = fd;
            }

            DateTime? toDate = null;
            if (!string.IsNullOrWhiteSpace(to) &&
                DateTime.TryParse(to, out var td))
            {
                toDate = td;
            }

            int skipVal = skip ?? 0;
            int takeVal = take ?? 50;

            var tradesTask = tradeRepo.GetTradesAsync(patternFilter, fromDate, toDate, skipVal, takeVal, ct);
            var countTask  = tradeRepo.GetTradeCountAsync(patternFilter, fromDate, toDate, ct);

            await Task.WhenAll(tradesTask, countTask);

            var trades     = await tradesTask;
            var totalCount = await countTask;

            return Results.Ok(new
            {
                TotalCount = totalCount,
                Skip       = skipVal,
                Take       = takeVal,
                Trades = trades.Select(t => new
                {
                    t.Id,
                    t.Symbol,
                    Pattern     = t.PatternType.ToString(),
                    t.EntryPrice,
                    t.ExitPrice,
                    t.Quantity,
                    t.PnL,
                    t.PnLPercent,
                    t.IsWin,
                    t.ExitReason,
                    EntryTime   = t.EntryTime.ToString("o"),
                    ExitTime    = t.ExitTime.ToString("o"),
                    HoldingDays = (t.ExitTime - t.EntryTime).Days
                })
            });
        }).RequireAuthorization();

        return group;
    }
}
