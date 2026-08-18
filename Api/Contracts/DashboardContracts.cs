using StockTrader.Application.Accounts;
using StockTrader.Application.Dashboard;

namespace StockTrader.Api.Contracts;

public sealed record DashboardAccountResponse(
    string AccountId,
    decimal TotalEquity,
    decimal Cash,
    decimal BuyingPower,
    decimal UnrealizedPnL,
    decimal DailyPnL,
    bool IsTradingBlocked,
    string StatusMessage,
    string FetchedAt)
{
    public static DashboardAccountResponse Create(
        ActiveBrokerAccountSnapshot account) => new(
        account.AccountId,
        account.TotalEquity,
        account.Cash,
        account.BuyingPower,
        account.UnrealizedPnL,
        account.DailyPnL,
        account.IsTradingBlocked,
        account.StatusMessage,
        account.FetchedAt.ToString("o"));
}

public sealed record DashboardRiskResponse(
    decimal DailyPnL,
    decimal DailyPnLPercent,
    decimal TotalUnrealizedPnL,
    bool IsTradingHalted,
    int OpenPositionCount,
    IReadOnlyDictionary<string, int> PositionsPerSector,
    string LastUpdated);

public sealed record DashboardRecommendationResponse(
    long Id,
    string Symbol,
    string Pattern,
    decimal EntryPrice,
    decimal StopLossPrice,
    decimal TargetPrice,
    decimal RiskRewardRatio,
    decimal Expectancy,
    bool WasExecuted,
    string GeneratedAt);

public sealed record DashboardResponse(
    DashboardAccountResponse? Account,
    DashboardRiskResponse Risk,
    int OpenPositionCount,
    int ActiveSignalCount,
    IReadOnlyList<DashboardRecommendationResponse> RecentRecommendations,
    IReadOnlyList<OpenPositionResponse> Positions,
    string MarketRegime,
    string OrderMode)
{
    public static DashboardResponse Create(DashboardSnapshot snapshot) => new(
        snapshot.Account is null
            ? null
            : DashboardAccountResponse.Create(snapshot.Account),
        new DashboardRiskResponse(
            snapshot.Risk.RiskState.DailyPnL,
            snapshot.Risk.RiskState.DailyPnLPercent,
            snapshot.Risk.TotalUnrealizedPnL,
            snapshot.Risk.RiskState.IsTradingHalted,
            snapshot.Risk.RiskState.OpenPositionCount,
            snapshot.Risk.RiskState.PositionsPerSector,
            snapshot.Risk.RiskState.LastUpdated.ToString("o")),
        snapshot.Risk.OpenPositions.Count,
        snapshot.Activity.ActiveSignalCount,
        snapshot.Activity.RecentRecommendations.Select(recommendation =>
            new DashboardRecommendationResponse(
                recommendation.Id,
                recommendation.Symbol,
                recommendation.Pattern,
                recommendation.EntryPrice,
                recommendation.StopLossPrice,
                recommendation.TargetPrice,
                recommendation.RiskRewardRatio,
                recommendation.Expectancy,
                recommendation.WasExecuted,
                recommendation.GeneratedAt.ToString("o"))).ToArray(),
        snapshot.Risk.OpenPositions.Positions
            .Select(OpenPositionResponseMapper.Map)
            .ToArray(),
        snapshot.MarketRegime,
        snapshot.Risk.Settings.OrderMode.ToString());
}
