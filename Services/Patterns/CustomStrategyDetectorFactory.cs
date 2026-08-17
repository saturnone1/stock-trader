using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

/// <summary>
/// The sole production composition boundary for the custom-strategy evaluation pipeline.
/// </summary>
public sealed class CustomStrategyDetectorFactory : ICustomStrategyDetectorFactory
{
    private readonly IIndicatorService _indicators;
    private readonly TimeProvider _timeProvider;

    public CustomStrategyDetectorFactory(IIndicatorService indicators, TimeProvider timeProvider)
    {
        _indicators = indicators;
        _timeProvider = timeProvider;
    }

    public ICustomStrategyDetector Create(CustomPatternDefinition definition) =>
        new RuleBasedDetector(_indicators, definition, _timeProvider);

    public ICustomStrategyDetector Create(CompiledStrategy strategy) =>
        new RuleBasedDetector(_indicators, strategy, _timeProvider);
}
