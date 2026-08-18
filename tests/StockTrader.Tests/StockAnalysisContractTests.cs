using System.Text.Json;
using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class StockAnalysisContractTests
{
    [Fact]
    public void ResponsePreservesValuesAndUsesCentralPatternDisplayName()
    {
        var source = new StockAnalysis
        {
            Symbol = "TQQQ",
            CurrentPrice = 100m,
            Grade = RecommendationGrade.Buy,
            UpsideProbability = 65m,
            ExpectedReturnPercent = 4.5m,
            ExpectedHoldingDays = 8,
            DownsideRiskPercent = 2m,
            RecommendedStopLoss = 95m,
            RecommendedTarget = 110m,
            ConfidenceScore = 72m,
            ATR = 2.5m,
            AnalyzedAt = new DateTime(2026, 8, 19, 1, 2, 3, DateTimeKind.Utc),
            Indicators = new IndicatorSnapshot { RSI = 31m, SMA200 = 90m },
            ActivePatterns =
            [
                new PatternSignalInfo
                {
                    PatternType = PatternType.Breakout,
                    Confidence = 0.8m,
                    HistoricalWinRate = 0.625m,
                    HistoricalAvgReturn = 3.2m,
                },
            ],
        };

        var response = StockAnalysisResponse.Create(source);

        response.Symbol.Should().Be("TQQQ");
        response.Grade.Should().Be("Buy");
        response.Atr.Should().Be(2.5m);
        response.Indicators.Rsi.Should().Be(31m);
        response.ActivePatterns.Should().ContainSingle().Which.Should().Be(
            new StockAnalysisPatternResponse(
                "Breakout", "가격 돌파", 0.8m, 0.625m, 3.2m));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(
            JsonSerializerDefaults.Web));
        json.Should().Contain("\"currentPrice\":100");
        json.Should().NotContain("\"CurrentPrice\"");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("activePatterns")[0]
            .GetProperty("patternName").GetString().Should().Be("가격 돌파");
    }
}
