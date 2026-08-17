using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Runtime contract shared by preview, backtest, optimization, scanning, and live exits for one
/// compiled custom strategy.
/// </summary>
public interface ICustomStrategyDetector : IPatternDetector, ICompiledStrategyRuntime
{
    string CustomPatternName { get; }
    CustomPatternDefinition Definition { get; }
}

public interface ICustomStrategyDetectorFactory
{
    ICustomStrategyDetector Create(CustomPatternDefinition definition);
    ICustomStrategyDetector Create(CompiledStrategy strategy);
}
