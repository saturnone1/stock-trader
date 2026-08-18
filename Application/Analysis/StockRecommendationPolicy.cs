using StockTrader.Models;

namespace StockTrader.Application.Analysis;

public sealed record StockRecommendationInput(
    decimal CurrentPrice,
    decimal Atr,
    IReadOnlyList<PatternSignalInfo> ActivePatterns,
    IndicatorSnapshot Indicators,
    decimal VolumeRatio,
    IReadOnlyList<PatternStats> PatternStats);

public sealed record StockRecommendation(
    decimal UpsideProbability,
    decimal ExpectedReturnPercent,
    decimal DownsideRiskPercent,
    decimal RecommendedStopLoss,
    decimal RecommendedTarget,
    decimal ConfidenceScore,
    RecommendationGrade Grade);

public static class StockRecommendationPolicy
{
    public static StockRecommendation Evaluate(StockRecommendationInput input)
    {
        var probability = ComputeUpsideProbability(input.ActivePatterns, input.Indicators);
        var expectedReturn = ComputeExpectedReturn(input.ActivePatterns, input.CurrentPrice, input.Atr);
        var downsideRisk = ComputeDownsideRisk(input.ActivePatterns, input.PatternStats);
        var stopLoss = ComputeRecommendedStopLoss(
            input.CurrentPrice,
            input.Atr,
            input.ActivePatterns,
            input.PatternStats);
        var target = ComputeRecommendedTarget(input.CurrentPrice, input.Atr, input.ActivePatterns);
        var confidence = ComputeConfidenceScore(
            input.ActivePatterns,
            input.Indicators,
            input.VolumeRatio,
            input.PatternStats);

        return new StockRecommendation(
            probability,
            expectedReturn,
            downsideRisk,
            stopLoss,
            target,
            confidence,
            DetermineGrade(probability, confidence));
    }

    private static decimal ComputeUpsideProbability(
        IReadOnlyList<PatternSignalInfo> activePatterns,
        IndicatorSnapshot indicators)
    {
        if (activePatterns.Count == 0)
            return Math.Clamp(ApplyIndicatorAdjustment(50m, indicators), 5m, 95m);

        double likelihoodUp = 1.0;
        double likelihoodDown = 1.0;
        const double prior = 0.5;

        foreach (var pattern in activePatterns)
        {
            var winRate = (double)Math.Clamp(pattern.HistoricalWinRate, 0.1m, 0.9m);
            likelihoodUp *= winRate;
            likelihoodDown *= 1.0 - winRate;
        }

        var posterior = (likelihoodUp * prior) /
                        (likelihoodUp * prior + likelihoodDown * (1.0 - prior));
        var probability = ApplyIndicatorAdjustment((decimal)(posterior * 100.0), indicators);
        return Math.Clamp(Math.Round(probability, 1), 5m, 95m);
    }

    private static decimal ApplyIndicatorAdjustment(decimal probability, IndicatorSnapshot indicators)
    {
        if (indicators.RSI > 0 && indicators.RSI < 30)
            probability *= 1.10m;
        else if (indicators.RSI > 70)
            probability *= 0.90m;

        if (indicators.SMA200 > 0 && indicators.SMA20 > indicators.SMA200)
            probability *= 1.05m;

        if (indicators.MACD > indicators.MACDSignal)
            probability *= 1.05m;

        if (indicators.BollingerLower > 0 && indicators.SMA20 > 0)
        {
            var width = indicators.BollingerUpper - indicators.BollingerLower;
            if (width > 0)
            {
                var position = (indicators.SMA20 - indicators.BollingerLower) / width;
                if (position < 0.2m) probability *= 1.05m;
                else if (position > 0.8m) probability *= 0.95m;
            }
        }

        return probability;
    }

    private static decimal ComputeExpectedReturn(
        IReadOnlyList<PatternSignalInfo> activePatterns,
        decimal currentPrice,
        decimal atr)
    {
        if (currentPrice == 0) return 0;

        var atrTargetReturn = 2.5m * atr / currentPrice * 100m;
        if (activePatterns.Count == 0)
            return Math.Round(atrTargetReturn * 0.5m, 2);

        var totalWeight = activePatterns.Sum(pattern => pattern.Confidence);
        var patternAverage = totalWeight > 0
            ? activePatterns.Sum(pattern => pattern.HistoricalAvgReturn * pattern.Confidence) / totalWeight
            : 0;

        return Math.Round(patternAverage * 0.6m + atrTargetReturn * 0.4m, 2);
    }

    private static decimal ComputeDownsideRisk(
        IReadOnlyList<PatternSignalInfo> activePatterns,
        IReadOnlyList<PatternStats> allStats)
    {
        if (activePatterns.Count == 0) return 50m;

        var totalWeight = 0m;
        var weightedLossRate = 0m;
        var weightedAverageLoss = 0m;

        foreach (var pattern in activePatterns)
        {
            var stats = allStats.FirstOrDefault(candidate => candidate.PatternType == pattern.PatternType);
            if (stats is null) continue;

            weightedLossRate += (1 - stats.WinRate) * pattern.Confidence;
            weightedAverageLoss += stats.AvgLossPercent * pattern.Confidence;
            totalWeight += pattern.Confidence;
        }

        if (totalWeight == 0) return 50m;
        return Math.Round(weightedLossRate / totalWeight * (weightedAverageLoss / totalWeight) * 100m, 1);
    }

    private static decimal ComputeRecommendedStopLoss(
        decimal currentPrice,
        decimal atr,
        IReadOnlyList<PatternSignalInfo> activePatterns,
        IReadOnlyList<PatternStats> allStats)
    {
        var atrStop = currentPrice - 2m * atr;
        if (activePatterns.Count == 0)
            return Math.Round(atrStop, 2);

        var averageDrawdown = activePatterns
            .Select(pattern => allStats.FirstOrDefault(stats => stats.PatternType == pattern.PatternType))
            .Where(stats => stats is not null)
            .Select(stats => stats!.MaxDrawdownPercent)
            .DefaultIfEmpty(0.05m)
            .Average();
        var maeStop = currentPrice * (1 - averageDrawdown);
        return Math.Round(Math.Max(atrStop, maeStop), 2);
    }

    private static decimal ComputeRecommendedTarget(
        decimal currentPrice,
        decimal atr,
        IReadOnlyList<PatternSignalInfo> activePatterns)
    {
        var atrTarget = currentPrice + 2.5m * atr;
        if (activePatterns.Count == 0)
            return Math.Round(atrTarget, 2);

        var totalWeight = activePatterns.Sum(pattern => pattern.Confidence);
        if (totalWeight <= 0)
            return Math.Round(atrTarget, 2);

        var averageReturn = activePatterns.Sum(
            pattern => pattern.HistoricalAvgReturn * pattern.Confidence) / totalWeight;
        var patternTarget = currentPrice * (1 + averageReturn / 100m);
        return Math.Round(patternTarget * 0.6m + atrTarget * 0.4m, 2);
    }

    private static decimal ComputeConfidenceScore(
        IReadOnlyList<PatternSignalInfo> activePatterns,
        IndicatorSnapshot indicators,
        decimal volumeRatio,
        IReadOnlyList<PatternStats> allStats)
    {
        var patternScore = activePatterns.Count > 0
            ? activePatterns.Average(pattern => pattern.HistoricalWinRate)
            : 0m;
        var confluenceScore = indicators.TotalIndicatorCount > 0
            ? (decimal)indicators.BullishIndicatorCount / indicators.TotalIndicatorCount
            : 0.5m;
        var volumeScore = Math.Min(1m, volumeRatio / 1.5m);
        var totalSamples = activePatterns
            .Select(pattern => allStats.FirstOrDefault(stats => stats.PatternType == pattern.PatternType))
            .Where(stats => stats is not null)
            .Sum(stats => stats!.SampleSize);
        var sampleScore = Math.Min(1m, totalSamples / 50m);
        var total = patternScore * 0.40m
                    + confluenceScore * 0.30m
                    + volumeScore * 0.15m
                    + sampleScore * 0.15m;

        return Math.Round(total * 100m, 0);
    }

    private static RecommendationGrade DetermineGrade(decimal probability, decimal confidence)
    {
        if (probability >= 70 && confidence >= 60) return RecommendationGrade.StrongBuy;
        if (probability >= 60 && confidence >= 45) return RecommendationGrade.Buy;
        if (probability >= 40) return RecommendationGrade.Neutral;
        if (probability >= 25) return RecommendationGrade.Sell;
        return RecommendationGrade.StrongSell;
    }
}
