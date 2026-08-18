using StockTrader.Application.Accounts;
using StockTrader.Application.Risk;

namespace StockTrader.Application.Dashboard;

public sealed record DashboardRecommendationSnapshot(
    long Id,
    string Symbol,
    string Pattern,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal RiskRewardRatio,
    decimal Expectancy,
    bool WasExecuted,
    DateTime GeneratedAt);

public sealed record DashboardActivitySnapshot(
    int ActiveSignalCount,
    IReadOnlyList<DashboardRecommendationSnapshot> RecentRecommendations);

public sealed record DashboardSnapshot(
    ActiveBrokerAccountSnapshot? Account,
    RiskOverviewSnapshot Risk,
    DashboardActivitySnapshot Activity,
    string MarketRegime);

public interface IDashboardActivityStore
{
    Task<DashboardActivitySnapshot> GetAsync(
        int recommendationCount,
        CancellationToken ct = default);
}

public interface IDashboardQuery
{
    Task<DashboardSnapshot> GetAsync(CancellationToken ct = default);
}
