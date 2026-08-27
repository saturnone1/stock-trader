using Microsoft.Extensions.Options;
using StockTrader.Application.Execution;
using StockTrader.Application.MarketData;
using StockTrader.Configuration;
using StockTrader.Domain.MarketData;

namespace StockTrader.BackgroundServices;

/// <summary>시장 개장 중 계좌별 포지션 실행 사이클을 정해진 주기로 호출합니다.</summary>
public sealed class PositionExecutionManagerService(
    IServiceScopeFactory scopeFactory,
    IMarketCalendar marketCalendar,
    IOptions<TradingSettings> settings,
    IOptions<TradingCoreTransportOptions> tradingCore,
    TimeProvider timeProvider,
    ILogger<PositionExecutionManagerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.Equals(tradingCore.Value.Mode, "Remote", StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Edge position evaluation is disabled; Trading Core owns autonomous protection");
            return;
        }
        logger.LogInformation("PositionExecutionManagerService started");
        var interval = TimeSpan.FromSeconds(
            settings.Value.PositionMonitoringIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (marketCalendar.IsMarketOpen(MarketRegion.UnitedStates))
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var cycle = scope.ServiceProvider
                        .GetRequiredService<ILivePositionMonitoringCycle>();
                    await cycle.RunAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "PositionExecutionManagerService error");
            }

            await Task.Delay(interval, timeProvider, stoppingToken);
        }

        logger.LogInformation("PositionExecutionManagerService stopped");
    }
}
