using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Models.Enums;
using StockTrader.Models;

namespace StockTrader.Services.TradingCore;

internal sealed class TradingCoreRemoteTradeHistoryStore(ITradingCoreControlPlane core)
    : ITradeHistoryStore
{
    public async Task<List<TradeRecord>> GetTradesAsync(
        PatternType? patternType = null,
        DateTime? from = null,
        DateTime? to = null,
        int skip = 0,
        int take = 1000,
        CancellationToken ct = default) => (await LoadAsync(ct))
            .Where(value => !patternType.HasValue || value.PatternType == patternType)
            .Where(value => !from.HasValue || value.EntryTime >= from)
            .Where(value => !to.HasValue || value.ExitTime <= to)
            .OrderByDescending(value => value.EntryTime)
            .Skip(Math.Max(0, skip))
            .Take(take > 0 ? take : 1000)
            .ToList();

    public async Task<int> GetTradeCountAsync(
        PatternType? patternType = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default) => (await LoadAsync(ct)).Count(value =>
            (!patternType.HasValue || value.PatternType == patternType)
            && (!from.HasValue || value.EntryTime >= from)
            && (!to.HasValue || value.ExitTime <= to));

    public async Task<List<TradeRecord>> GetRecentAsync(
        int limit = 5000,
        CancellationToken ct = default) => (await LoadAsync(ct))
            .OrderByDescending(value => value.EntryTime)
            .Take(limit)
            .ToList();

    private async Task<IReadOnlyList<TradeRecord>> LoadAsync(CancellationToken ct) =>
        (await core.GetPortfolioAsync(ct)).Trades
            .Select(TradingCoreProjectionMapper.Trade)
            .ToArray();
}

internal sealed class TradingCoreRemoteTradeActivityStore(ITradingCoreControlPlane core)
    : ITradeActivityStore
{
    public async Task<IReadOnlyList<TradeRecommendationActivity>> GetRecommendationsAsync(
        int count,
        CancellationToken ct = default) => (await core.GetPortfolioAsync(ct)).Recommendations
            .OrderByDescending(value => value.GeneratedAtUtc)
            .Take(count)
            .Select(value => new TradeRecommendationActivity(
                TradingCoreProjectionMapper.NullableId(value.SourceSignalId)
                    ?? TradingCoreProjectionMapper.ExternalId(value.RecommendationId),
                TradingCoreProjectionMapper.NullableId(value.SourceSignalId),
                value.Symbol,
                TradingCoreProjectionMapper.Pattern(value.PatternCode, value.CustomPatternName),
                value.CustomPatternName,
                value.EntryPrice,
                value.StopLossPrice,
                value.TargetPrice,
                value.EntryPrice * value.ShareQuantity,
                value.ShareQuantity,
                value.Expectancy,
                value.WasExecuted,
                Enum.TryParse<OrderMode>(value.Mode, true, out var mode) ? mode : OrderMode.AutoOrder,
                value.GeneratedAtUtc,
                value.EntryRequestedAtUtc,
                string.IsNullOrWhiteSpace(value.EntryAccountId)
                    ? null
                    : TradingCoreProjectionMapper.IntId(value.EntryAccountId),
                !string.IsNullOrWhiteSpace(value.EntryOrderId),
                value.EntryExecutionNote))
            .ToArray();

    public async Task<TradeHistorySlice> GetHistoryAsync(
        PatternType? patternType,
        DateTime? from,
        DateTime? to,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        var filtered = (await core.GetPortfolioAsync(ct)).Trades
            .Select(TradingCoreProjectionMapper.Trade)
            .Where(value => !patternType.HasValue || value.PatternType == patternType)
            .Where(value => !from.HasValue || value.EntryTime >= from)
            .Where(value => !to.HasValue || value.ExitTime <= to)
            .OrderByDescending(value => value.EntryTime)
            .ThenByDescending(value => value.Id)
            .ToArray();
        return new TradeHistorySlice(filtered.Length, filtered.Skip(skip).Take(take)
            .Select(value => new CompletedTradeActivity(
                value.Id, value.Symbol, value.PatternType, value.CustomPatternName,
                value.EntryPrice, value.ExitPrice, value.Quantity, value.PnL,
                value.PnLPercent, value.ExitReason, value.EntryTime, value.ExitTime))
            .ToArray());
    }
}
