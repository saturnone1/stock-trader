using System.Text.Json;
using FluentAssertions;
using StockTrader.Api;

namespace StockTrader.Tests;

public sealed class MlApiContractTests
{
    [Fact]
    public void StatusResponsePreservesTheEstablishedWireNames()
    {
        var response = new MlStatusResponse(
            new MlRegimeClassifierStatusResponse(
                true,
                "2026-08-19T00:00:00.0000000Z",
                250,
                new Dictionary<string, string> { ["1"] = "강세장" }),
            new MlSignalScorerStatusResponse(
                true,
                null,
                120,
                0.75,
                0.82,
                [new MlFeatureImportanceResponse("RSI", 0.4)]),
            true,
            "학습 중");

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var root = json.RootElement;

        root.GetProperty("regimeClassifier")
            .GetProperty("isRegimeModelLoaded").GetBoolean().Should().BeTrue();
        root.GetProperty("regimeClassifier")
            .GetProperty("regimeTrainingSamples").GetInt32().Should().Be(250);
        root.GetProperty("signalScorer")
            .GetProperty("isSignalScorerLoaded").GetBoolean().Should().BeTrue();
        root.GetProperty("signalScorer")
            .GetProperty("signalScorerAuc").GetDouble().Should().Be(0.82);
        root.GetProperty("isTraining").GetBoolean().Should().BeTrue();
        root.GetProperty("trainingStatus").GetString().Should().Be("학습 중");
    }
}
