using Microsoft.AspNetCore.Mvc;
using StockTrader.Api.Contracts;
using StockTrader.Data.Repositories;
using StockTrader.Services.Account;
using StockTrader.Services.Analysis;
using StockTrader.Services.Risk;

namespace StockTrader.Api;

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardApi(this RouteGroupBuilder group)
    {
        group.MapGet("/dashboard", async (
            IAccountManager accountManager,
            IRiskManagementService riskService,
            ITradeRepository tradeRepo,
            IStockAnalysisService analysisService,
            ISettingsRepository settingsRepo,
            TimeProvider timeProvider,
            CancellationToken ct) =>
        {
            // 병렬로 데이터 수집
            var accountTask      = accountManager.GetActiveBrokerServiceAsync(ct);
            var riskTask         = riskService.GetCurrentRiskStateAsync(ct);
            var positionsTask    = tradeRepo.GetOpenPositionsAsync(ct);
            var signalsTask      = tradeRepo.GetActiveSignalsAsync(ct);
            var recsTask         = tradeRepo.GetRecentRecommendationsAsync(5, ct);
            var regimeTask       = analysisService.GetMarketRegimeAsync(ct);
            var settingsTask     = settingsRepo.GetAsync(ct);

            await Task.WhenAll(accountTask, riskTask, positionsTask, signalsTask, recsTask, regimeTask, settingsTask);

            var broker    = await accountTask;
            var riskState = await riskTask;
            var positions = await positionsTask;
            var signals   = await signalsTask;
            var recs      = await recsTask;
            var regime    = await regimeTask;
            var settings  = await settingsTask;

            // 브로커 계좌 정보 (없으면 null)
            object? accountInfo = null;
            if (broker != null)
            {
                try
                {
                    var brokerAccount = await broker.GetAccountAsync(ct);
                    if (brokerAccount != null)
                    accountInfo = new
                    {
                        brokerAccount.AccountId,
                        brokerAccount.TotalEquity,
                        brokerAccount.Cash,
                        brokerAccount.BuyingPower,
                        brokerAccount.UnrealizedPnL,
                        brokerAccount.DailyPnL,
                        brokerAccount.IsTradingBlocked,
                        brokerAccount.StatusMessage,
                        FetchedAt = brokerAccount.FetchedAt.ToString("o")
                    };
                }
                catch
                {
                    // 브로커 연결 실패 시 null 유지
                }
            }

            return Results.Ok(new
            {
                Account = accountInfo,
                RiskState = new
                {
                    riskState.DailyPnL,
                    riskState.DailyPnLPercent,
                    riskState.IsTradingHalted,
                    riskState.OpenPositionCount,
                    riskState.PositionsPerSector,
                    LastUpdated = riskState.LastUpdated.ToString("o")
                },
                OpenPositionCount  = positions.Count,
                ActiveSignalCount  = signals.Count,
                RecentSignals = recs.Select(r => new
                {
                    r.Id,
                    r.Symbol,
                    Pattern     = r.PatternType.ToString(),
                    r.EntryPrice,
                    r.StopLossPrice,
                    r.TargetPrice,
                    r.RiskRewardRatio,
                    r.Expectancy,
                    r.WasExecuted,
                    GeneratedAt = r.GeneratedAt.ToString("o")
                }),
                Positions = positions.Select(p => OpenPositionResponseMapper.Map(
                    p, timeProvider.GetUtcNow().UtcDateTime)),
                MarketRegime = regime.RegimeLabel,
                OrderMode    = settings.OrderMode.ToString()
            });
        }).RequireAuthorization();

        return group;
    }
}
