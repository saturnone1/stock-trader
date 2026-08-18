using FluentAssertions;
using StockTrader.Application.Analysis;
using StockTrader.Models;

namespace StockTrader.Tests;

public class StockRecommendationPolicyTests
{
    [Fact]
    public void Evaluate_PreservesNeutralNoPatternBaseline()
    {
        var recommendation = StockRecommendationPolicy.Evaluate(new StockRecommendationInput(
            CurrentPrice: 100m,
            Atr: 2m,
            ActivePatterns: [],
            Indicators: new IndicatorSnapshot(),
            VolumeRatio: 1.5m,
            PatternStats: []));

        recommendation.Should().Be(new StockRecommendation(
            UpsideProbability: 50m,
            ExpectedReturnPercent: 2.5m,
            DownsideRiskPercent: 50m,
            RecommendedStopLoss: 96m,
            RecommendedTarget: 105m,
            ConfidenceScore: 30m,
            Grade: RecommendationGrade.Neutral));
    }

    [Fact]
    public void Evaluate_PreservesPatternIndicatorAndRiskWeighting()
    {
        var patterns = new[]
        {
            new PatternSignalInfo
            {
                PatternType = PatternType.RsiMeanReversion,
                Confidence = 0.8m,
                HistoricalWinRate = 0.75m,
                HistoricalAvgReturn = 8m
            }
        };
        var statistics = new[]
        {
            new PatternStats
            {
                PatternType = PatternType.RsiMeanReversion,
                WinRate = 0.75m,
                AvgLossPercent = 0.04m,
                MaxDrawdownPercent = 0.06m,
                SampleSize = 50
            }
        };
        var indicators = new IndicatorSnapshot
        {
            RSI = 25m,
            SMA20 = 110m,
            SMA200 = 100m,
            MACD = 2m,
            MACDSignal = 1m,
            BollingerLower = 90m,
            BollingerUpper = 120m,
            BullishIndicatorCount = 3,
            TotalIndicatorCount = 4
        };

        var recommendation = StockRecommendationPolicy.Evaluate(new StockRecommendationInput(
            CurrentPrice: 100m,
            Atr: 2m,
            ActivePatterns: patterns,
            Indicators: indicators,
            VolumeRatio: 1.5m,
            PatternStats: statistics));

        recommendation.Should().Be(new StockRecommendation(
            UpsideProbability: 91m,
            ExpectedReturnPercent: 6.8m,
            DownsideRiskPercent: 1m,
            RecommendedStopLoss: 96m,
            RecommendedTarget: 106.8m,
            ConfidenceScore: 82m,
            Grade: RecommendationGrade.StrongBuy));
    }
}
