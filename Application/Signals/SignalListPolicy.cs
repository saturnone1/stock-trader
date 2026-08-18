using StockTrader.Application.Statistics;
using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Signals;

public static class SignalListPolicy
{
    public static SignalListSnapshot Evaluate(
        IEnumerable<BrowsableSignal> source,
        IReadOnlyList<PatternStatisticsSnapshot> statistics,
        SignalBrowseRequest request)
    {
        var signals = source;
        if (!string.IsNullOrWhiteSpace(request.Pattern)
            && Enum.TryParse<PatternType>(
                request.Pattern,
                ignoreCase: true,
                out var pattern))
        {
            signals = signals.Where(signal => signal.PatternType == pattern);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            signals = signals.Where(signal => signal.Symbol.Contains(
                request.Search,
                StringComparison.OrdinalIgnoreCase));
        }

        var projected = signals.Select(signal =>
        {
            var riskReward = CalculateRiskReward(signal);
            var statistic = PatternStatisticsSelectionPolicy.Resolve(
                signal.PatternType,
                signal.Symbol,
                statistics);
            return new SignalListItem(
                signal.Id,
                signal.Symbol,
                signal.PatternType.ToString(),
                signal.EntryPrice,
                signal.StopLossPrice,
                signal.TargetPrice,
                signal.Confidence,
                riskReward,
                signal.Details,
                signal.DetectedAt,
                statistic?.WinRate,
                statistic?.Expectancy);
        });

        projected = request.Sort?.ToLowerInvariant() switch
        {
            "confidence" => projected
                .OrderByDescending(signal => signal.Confidence)
                .ThenByDescending(signal => signal.DetectedAt)
                .ThenByDescending(signal => signal.Id),
            "rr" => projected
                .OrderByDescending(signal => signal.RiskReward)
                .ThenByDescending(signal => signal.DetectedAt)
                .ThenByDescending(signal => signal.Id),
            _ => projected
                .OrderByDescending(signal => signal.DetectedAt)
                .ThenByDescending(signal => signal.Id)
        };

        return new SignalListSnapshot(projected.ToArray());
    }

    private static decimal CalculateRiskReward(BrowsableSignal signal) =>
        RiskRewardRatioPolicy.CalculateLong(
            signal.EntryPrice,
            signal.StopLossPrice,
            signal.TargetPrice);

}
