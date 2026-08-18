using Microsoft.Extensions.Options;
using StockTrader.Application.Risk;
using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;

namespace StockTrader.Services.Risk;

public sealed class RiskOverviewQuery(
    IRiskManagementService riskService,
    IOpenPositionStore positions,
    ISettingsRepository settings,
    IOptions<TradingSettings> tradingOptions,
    TimeProvider timeProvider)
    : IRiskOverviewQuery
{
    public async Task<RiskOverviewSnapshot> GetAsync(CancellationToken ct = default)
    {
        var riskTask = riskService.GetCurrentRiskStateAsync(ct);
        var positionsTask = positions.GetOpenPositionsAsync(ct);
        var settingsTask = settings.GetAsync(ct);
        await Task.WhenAll(riskTask, positionsTask, settingsTask);

        var risk = await riskTask;
        var openPositions = await positionsTask;
        var userSettings = await settingsTask;
        var trading = tradingOptions.Value;
        var observedAt = timeProvider.GetUtcNow().UtcDateTime;
        var positionRisks = openPositions.Select(position =>
        {
            var metrics = PositionRiskProjectionPolicy.Evaluate(
                position.EntryPrice,
                position.CurrentPrice,
                position.StopLossPrice,
                position.OpenedAt,
                observedAt);
            return new PositionRiskSnapshot(
                position.Symbol,
                position.PatternType.ToString(),
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
            openPositions.Sum(position => position.UnrealizedPnL));
    }
}
