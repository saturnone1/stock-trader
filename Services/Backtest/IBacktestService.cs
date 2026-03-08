using StockTrader.Models;

namespace StockTrader.Services.Backtest;

public interface IBacktestService
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default);
    Task<Api.OptimizeResponse> RunOptimizationAsync(Api.OptimizeRequest request, CancellationToken ct = default);
}
