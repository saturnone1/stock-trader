using Microsoft.Extensions.Options;
using StockTrader.Application.Portfolio;
using StockTrader.Application.Risk;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;

namespace StockTrader.Services.Risk;

public sealed class RiskOverviewQuery(
    IRiskManagementService riskService,
    IOpenPositionQuery positions,
    ISettingsRepository settings,
    IOptions<TradingSettings> tradingOptions)
    : IRiskOverviewQuery
{
    public async Task<RiskOverviewSnapshot> GetAsync(CancellationToken ct = default)
    {
        var riskTask = riskService.GetCurrentRiskStateAsync(ct);
        var positionsTask = positions.GetAsync(ct);
        var settingsTask = settings.GetAsync(ct);
        await Task.WhenAll(riskTask, positionsTask, settingsTask);

        var risk = await riskTask;
        var openPositions = await positionsTask;
        var userSettings = await settingsTask;
        var trading = tradingOptions.Value;
        var positionRisks = openPositions.Positions.Select(position =>
        {
            var metrics = PositionRiskProjectionPolicy.Evaluate(
                position.EntryPrice,
                position.CurrentPrice,
                position.StopLossPrice,
                position.OpenedAt,
                openPositions.ObservedAt);
            return new PositionRiskSnapshot(
                position.Symbol,
                position.Pattern,
                position.EntryPrice,
                position.CurrentPrice,
                position.StopLossPrice,
                metrics.RiskPerShare,
                metrics.RMultiple,
                position.UnrealizedPnL,
                metrics.HoldingDays);
        }).ToArray();

        return new RiskOverviewSnapshot(
            new RiskStateSnapshot(
                risk.DailyPnL,
                risk.DailyPnLPercent,
                risk.IsTradingHalted,
                risk.OpenPositionCount,
                risk.PositionsPerSector,
                risk.LastUpdated),
            new RiskSettingsSnapshot(
                userSettings.AccountSize,
                userSettings.RiskPerTradePercent,
                userSettings.DailyLossLimitPercent,
                userSettings.MaxTotalPositions,
                userSettings.MaxPositionsPerSector,
                userSettings.MinExpectancy,
                trading.MinConfidence),
            positionRisks,
            openPositions.TotalUnrealizedPnL);
    }
}
