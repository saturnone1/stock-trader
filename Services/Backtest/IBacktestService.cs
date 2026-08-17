using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

public interface IBacktestService
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default);
    Task<OptimizeResponse> RunOptimizationAsync(OptimizeRequest request, CancellationToken ct = default);
}
