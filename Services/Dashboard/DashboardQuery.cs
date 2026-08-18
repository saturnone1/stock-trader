using StockTrader.Application.Accounts;
using StockTrader.Application.Dashboard;
using StockTrader.Application.Risk;
using StockTrader.Application.Signals;
using StockTrader.Services.Analysis;

namespace StockTrader.Services.Dashboard;

public sealed class DashboardQuery(
    IActiveBrokerAccountQuery account,
    IRiskOverviewQuery risk,
    IDashboardActivityStore activity,
    IStockAnalysisService analysis,
    SignalFreshnessPolicy signalFreshness,
    TimeProvider timeProvider) : IDashboardQuery
{
    private const int RecentRecommendationCount = 5;

    public async Task<DashboardSnapshot> GetAsync(CancellationToken ct = default)
    {
        var accountTask = account.GetAsync(ct);
        var riskTask = risk.GetAsync(ct);
        var signalWindow = signalFreshness.GetWindow(
            timeProvider.GetUtcNow().UtcDateTime);
        var activityTask = activity.GetAsync(
            RecentRecommendationCount,
            signalWindow.DetectedFromInclusiveUtc,
            signalWindow.DetectedThroughInclusiveUtc,
            ct);
        var regimeTask = analysis.GetMarketRegimeAsync(ct);
        await Task.WhenAll(accountTask, riskTask, activityTask, regimeTask);

        return new DashboardSnapshot(
            await accountTask,
            await riskTask,
            await activityTask,
            (await regimeTask).RegimeLabel);
    }
}
