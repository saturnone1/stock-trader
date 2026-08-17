using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Runtime contract shared by preview, backtest, optimization, scanning, and live exits for one
/// compiled custom strategy.
/// </summary>
public interface ICustomStrategyDetector : IPatternDetector, ICompiledStrategyRuntime
{
    string CustomPatternName { get; }
    StrategyDocument Definition { get; }
}

public interface ICustomStrategyDetectorFactory
{
    ICustomStrategyDetector Create(StrategyDocument definition);
    ICustomStrategyDetector Create(CompiledStrategy strategy);
}
