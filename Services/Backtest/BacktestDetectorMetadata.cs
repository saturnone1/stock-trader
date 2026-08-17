using StockTrader.Services.Patterns;

namespace StockTrader.Services.Backtest;

internal static class BacktestDetectorMetadata
{
    public static IReadOnlyCollection<string> CollectReferenceSymbols(
        IEnumerable<IPatternDetector> detectors) =>
        detectors
            .OfType<ICustomStrategyDetector>()
            .SelectMany(detector => detector.Strategy.ReferenceSymbols)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
