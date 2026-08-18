using StockTrader.Application.Strategies;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// The sole production composition boundary for the custom-strategy evaluation pipeline.
/// </summary>
public sealed class CustomStrategyDetectorFactory : ICustomStrategyDetectorFactory
{
    private readonly IIndicatorService _indicators;

    public CustomStrategyDetectorFactory(IIndicatorService indicators)
    {
        _indicators = indicators;
    }

    public ICustomStrategyDetector Create(StrategyDocument definition) =>
        new RuleBasedDetector(_indicators, definition);

    public ICustomStrategyDetector Create(CompiledStrategy strategy) =>
        new RuleBasedDetector(_indicators, strategy);
}
