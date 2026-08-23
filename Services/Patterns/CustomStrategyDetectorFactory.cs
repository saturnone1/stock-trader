using StockTrader.Application.Strategies;

namespace StockTrader.Services.Patterns;

/// <summary>
/// The sole production composition boundary for the custom-strategy evaluation pipeline.
/// </summary>
public sealed class CustomStrategyDetectorFactory : ICustomStrategyDetectorFactory
{
    public ICustomStrategyDetector Create(StrategyDocument definition) =>
        new RuleBasedDetector(definition);

    public ICustomStrategyDetector Create(CompiledStrategy strategy) =>
        new RuleBasedDetector(strategy);
}
