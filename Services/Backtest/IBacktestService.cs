using StockTrader.Models;

namespace StockTrader.Services.Backtest;

public interface IBacktestService
{
    Task<BacktestResult> RunAsync(BacktestRequest request, CancellationToken ct = default);
}
