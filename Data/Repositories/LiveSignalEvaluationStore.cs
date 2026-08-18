using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Execution;
using StockTrader.Application.Signals;

namespace StockTrader.Data.Repositories;

/// <summary>실시간 신호 평가에 필요한 영속 상태만 투영하는 EF Core 읽기 어댑터입니다.</summary>
public sealed class LiveSignalEvaluationStore(AppDbContext db) : ILiveSignalEvaluationStore
{
    public async Task<LiveSignalEvaluationSnapshot> LoadAsync(
        IReadOnlyCollection<string> strategyNames,
        IReadOnlyCollection<string> symbols,
        DateTime marketSessionStartUtc,
        CancellationToken ct = default)
    {
        var strategyKeys = strategyNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        IReadOnlyDictionary<string, IReadOnlyList<StrategyCompletedTrade>> completedTrades;
        IReadOnlyDictionary<string, int> executedEntries;
        var openPositionCount = 0;
        if (strategyKeys.Length == 0)
        {
            completedTrades = CaseInsensitiveDictionary<IReadOnlyList<StrategyCompletedTrade>>();
            executedEntries = CaseInsensitiveDictionary<int>();
        }
        else
        {
            var tradeRows = await db.TradeRecords
                .AsNoTracking()
                .Where(trade => trade.CustomPatternName != null
                    && strategyKeys.Contains(trade.CustomPatternName.ToUpper()))
                .OrderBy(trade => trade.ExitTime)
                .ThenBy(trade => trade.Id)
                .Select(trade => new
                {
                    StrategyName = trade.CustomPatternName!,
                    trade.Id,
                    trade.ExitTime,
                    trade.PnL,
                    trade.PnLPercent
                })
                .ToListAsync(ct);
            completedTrades = tradeRows
                .GroupBy(row => row.StrategyName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<StrategyCompletedTrade>)group
                        .Select(row => new StrategyCompletedTrade(
                            row.Id,
                            row.ExitTime,
                            row.PnL,
                            row.PnLPercent))
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            openPositionCount = await db.Positions
                .AsNoTracking()
                .CountAsync(position => position.ClosedAt == null, ct);

            var executedNames = await db.TradeRecommendations
                .AsNoTracking()
                .Where(recommendation => !recommendation.IsSuperseded
                    && recommendation.WasExecuted
                    && recommendation.GeneratedAt >= marketSessionStartUtc
                    && recommendation.CustomPatternName != null
                    && strategyKeys.Contains(recommendation.CustomPatternName.ToUpper()))
                .Select(recommendation => recommendation.CustomPatternName!)
                .ToListAsync(ct);
            executedEntries = executedNames
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.OrdinalIgnoreCase);
        }

        var symbolKeys = symbols
            .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(symbol => symbol.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyDictionary<string, string> sectors;
        if (symbolKeys.Length == 0)
        {
            sectors = CaseInsensitiveDictionary<string>();
        }
        else
        {
            var sectorRows = await db.Tickers
                .AsNoTracking()
                .Where(ticker => symbolKeys.Contains(ticker.Symbol.ToUpper()))
                .Select(ticker => new { ticker.Symbol, ticker.Sector })
                .ToListAsync(ct);
            sectors = sectorRows.ToDictionary(
                row => row.Symbol,
                row => row.Sector,
                StringComparer.OrdinalIgnoreCase);
        }

        return new LiveSignalEvaluationSnapshot(
            completedTrades,
            openPositionCount,
            executedEntries,
            sectors);
    }

    private static Dictionary<string, TValue> CaseInsensitiveDictionary<TValue>() =>
        new(StringComparer.OrdinalIgnoreCase);
}
