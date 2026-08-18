using StockTrader.Application.Trading;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Services.Risk;
using Microsoft.Extensions.Options;

namespace StockTrader.Api;

public static class RiskEndpoints
{
    public static RouteGroupBuilder MapRiskApi(this RouteGroupBuilder group)
    {
        // GET /api/risk — 리스크 상태 전체
        group.MapGet("/risk", async (
            IRiskManagementService riskService,
            IOpenPositionStore positionsStore,
            ISettingsRepository settingsRepo,
            IOptions<TradingSettings> tradingOptions,
            CancellationToken ct) =>
        {
            var riskTask      = riskService.GetCurrentRiskStateAsync(ct);
            var positionsTask = positionsStore.GetOpenPositionsAsync(ct);
            var settingsTask  = settingsRepo.GetAsync(ct);

            await Task.WhenAll(riskTask, positionsTask, settingsTask);

            var riskState = await riskTask;
            var positions = await positionsTask;
            var settings  = await settingsTask;
            var trading   = tradingOptions.Value;

            // 포지션별 R배수 계산
            var positionRMultiples = positions.Select(p =>
            {
                decimal riskPerShare = p.EntryPrice - p.StopLossPrice;
                decimal rMultiple = riskPerShare != 0
                    ? (p.CurrentPrice - p.EntryPrice) / Math.Abs(riskPerShare)
                    : 0m;

                return new
                {
                    p.Symbol,
                    Pattern         = p.PatternType.ToString(),
                    p.EntryPrice,
                    p.CurrentPrice,
                    p.StopLossPrice,
                    RiskPerShare    = riskPerShare,
                    RMultiple       = rMultiple,
                    p.UnrealizedPnL,
                    HoldingDays     = (DateTime.UtcNow - p.OpenedAt).Days
                };
            }).ToList();

            return Results.Ok(new
            {
                RiskState = new
                {
                    riskState.DailyPnL,
                    riskState.DailyPnLPercent,
                    riskState.IsTradingHalted,
                    riskState.OpenPositionCount,
                    riskState.PositionsPerSector,
                    LastUpdated = riskState.LastUpdated.ToString("o")
                },
                Settings = new
                {
                    settings.AccountSize,
                    settings.RiskPerTradePercent,
                    settings.DailyLossLimitPercent,
                    settings.MaxTotalPositions,
                    settings.MaxPositionsPerSector,
                    settings.MinExpectancy,
                    MinConfidence = trading.MinConfidence
                },
                PositionRMultiples = positionRMultiples,
                TotalUnrealizedPnL = positions.Sum(p => p.UnrealizedPnL)
            });
        }).RequireAuthorization();

        return group;
    }
}
