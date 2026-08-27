using StockTrader.Application.Accounts;
using StockTrader.Application.Dashboard;
using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Domain.Trading;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.TradingCore;

internal sealed class TradingCoreRemoteAccountQuery(ITradingCoreControlPlane core)
    : IActiveBrokerAccountQuery
{
    public async Task<ActiveBrokerAccountSnapshot?> GetAsync(CancellationToken ct = default)
    {
        var portfolio = await core.GetPortfolioAsync(ct);
        var account = portfolio.Accounts.OrderBy(value => value.AccountId, StringComparer.Ordinal)
            .FirstOrDefault();
        return account is null ? null : new ActiveBrokerAccountSnapshot(
            account.AccountId,
            account.TotalEquity,
            account.Cash,
            account.BuyingPower,
            account.UnrealizedPnL,
            account.DailyPnL,
            account.IsTradingBlocked,
            account.IsTradingBlocked ? "Trading blocked" : "Active",
            account.ObservedAtUtc);
    }
}

internal sealed class TradingCoreRemoteRecommendationStore(ITradingCoreControlPlane core)
    : ITradeRecommendationStore
{
    public async Task<List<TradeRecommendation>> GetRecentRecommendationsAsync(
        int count = 20,
        CancellationToken ct = default) => (await core.GetPortfolioAsync(ct)).Recommendations
            .OrderByDescending(value => value.GeneratedAtUtc)
            .Take(Math.Max(0, count))
            .Select(Map)
            .ToList();

    public Task AddRecommendationAsync(
        TradeRecommendation recommendation,
        CancellationToken ct = default) =>
        throw new InvalidOperationException("remote-trading-core-recommendation-store-is-read-only");

    internal static TradeRecommendation Map(
        ServiceContracts.TradingCore.TradingRecommendationProjection value) => new()
    {
        Id = TradingCoreProjectionMapper.ExternalId(value.RecommendationId),
        SourceSignalId = TradingCoreProjectionMapper.NullableId(value.SourceSignalId),
        Symbol = value.Symbol,
        PatternType = TradingCoreProjectionMapper.Pattern(value.PatternCode, value.CustomPatternName),
        CustomPatternName = value.CustomPatternName,
        GeneratedAt = value.GeneratedAtUtc,
        EntryPrice = value.EntryPrice,
        StopLossPrice = value.StopLossPrice,
        TargetPrice = value.TargetPrice,
        PositionSize = value.EntryPrice * value.ShareQuantity,
        ShareQuantity = value.ShareQuantity,
        Expectancy = value.Expectancy,
        WasExecuted = value.WasExecuted,
        Mode = Enum.TryParse<OrderMode>(value.Mode, true, out var mode) ? mode : OrderMode.AutoOrder,
        EntryRequestedAt = value.EntryRequestedAtUtc,
        EntryAccountId = string.IsNullOrWhiteSpace(value.EntryAccountId)
            ? null
            : TradingCoreProjectionMapper.IntId(value.EntryAccountId),
        EntryOrderId = value.EntryOrderId,
        EntryExecutionNote = value.EntryExecutionNote,
    };
}

internal sealed class TradingCoreRemoteDashboardActivityStore(
    ITradingCoreControlPlane core,
    IPatternSignalStore signals) : IDashboardActivityStore
{
    public async Task<DashboardActivitySnapshot> GetAsync(
        int recommendationCount,
        DateTime signalDetectedFromInclusiveUtc,
        DateTime signalDetectedThroughInclusiveUtc,
        CancellationToken ct = default)
    {
        var signalTask = signals.GetActionableSignalsAsync(
            signalDetectedFromInclusiveUtc, signalDetectedThroughInclusiveUtc, ct);
        var portfolioTask = core.GetPortfolioAsync(ct);
        await Task.WhenAll(signalTask, portfolioTask);
        var recommendations = (await portfolioTask).Recommendations
            .OrderByDescending(value => value.GeneratedAtUtc)
            .Take(Math.Max(0, recommendationCount))
            .Select(value => new DashboardRecommendationSnapshot(
                TradingCoreProjectionMapper.ExternalId(value.RecommendationId),
                value.Symbol,
                TradingCoreProjectionMapper.Pattern(value.PatternCode, value.CustomPatternName).ToString(),
                value.EntryPrice,
                value.StopLossPrice,
                value.TargetPrice,
                RiskRewardRatioPolicy.CalculateWithAbsoluteStopDistance(
                    value.EntryPrice, value.StopLossPrice, value.TargetPrice),
                value.Expectancy,
                value.WasExecuted,
                value.GeneratedAtUtc))
            .ToArray();
        return new DashboardActivitySnapshot((await signalTask).Count, recommendations);
    }
}
