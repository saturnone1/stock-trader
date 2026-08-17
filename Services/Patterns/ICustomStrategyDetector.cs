using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

/// <summary>
/// Runtime contract shared by preview, backtest, optimization, scanning, and live exits for one
/// compiled custom strategy.
/// </summary>
public interface ICustomStrategyDetector : IPatternDetector
{
    string CustomPatternName { get; }
    CustomPatternDefinition Definition { get; }
    CompiledStrategy Strategy { get; }
    bool HasExitRules { get; }
    bool HasScalingRules { get; }

    void SetReferenceData(Dictionary<string, OhlcvBar[]> referenceData, DateTime? asOf = null);
    bool ShouldExit(OhlcvBar[] bars);
    ScalingRule? CheckScaling(
        OhlcvBar[] bars,
        decimal currentProfitPercent,
        Dictionary<int, int> scaleCounts);
}

public interface ICustomStrategyDetectorFactory
{
    ICustomStrategyDetector Create(CustomPatternDefinition definition);
    ICustomStrategyDetector Create(CompiledStrategy strategy);
}
