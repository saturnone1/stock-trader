using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.ML;

namespace StockTrader.Tests;

public sealed class MarketRegimeModelIntegrityTests
{
    [Fact]
    public void FeatureFactoryOrdersBarsIgnoresFutureAndRejectsInvalidPriceWindows()
    {
        var bars = Bars(100);
        var asOf = bars[^1].Timestamp;
        var future = Bar(100, asOf.AddDays(1));
        future.Close = 100_000m;
        future.Volume = 100_000_000;

        var baseline = MarketRegimeFeatureFactory.CreateLatest(bars, asOf);
        var unorderedWithFuture = MarketRegimeFeatureFactory.CreateLatest(
            [future, .. bars.Reverse()], asOf);
        var invalid = bars.Select(Clone).ToArray();
        invalid[^10].Close = 0;

        baseline.Should().NotBeNull();
        unorderedWithFuture.Should().Be(baseline);
        MarketRegimeFeatureFactory.CreateLatest(invalid, asOf).Should().BeNull();
    }

    [Fact]
    public void ClusterLabelPolicyAssignsEveryInvestorRegimeExactlyOnce()
    {
        var labels = MarketRegimeClusterLabelPolicy.Assign(
        [
            new MarketRegimeClusterProfile(1, 0.20, 0.02),
            new MarketRegimeClusterProfile(2, -0.15, 0.03),
            new MarketRegimeClusterProfile(3, 0.01, 0.01),
            new MarketRegimeClusterProfile(4, 0.30, 0.20),
        ]);

        labels[1].Should().Be(MarketRegimeClusterCatalog.Bullish);
        labels[2].Should().Be(MarketRegimeClusterCatalog.Bearish);
        labels[3].Should().Be(MarketRegimeClusterCatalog.Sideways);
        labels[4].Should().Be(MarketRegimeClusterCatalog.HighVolatility);
        labels.Values.Should().BeEquivalentTo(MarketRegimeClusterCatalog.Labels);
    }

    [Fact]
    public async Task ClassifierRejectsLegacyMismatchedAndTamperedArtifacts()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"stocktrader-regime-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new MLSettings
            {
                ModelDirectory = directory,
                RegimeModelFileName = "regime.zip",
                RegimeClusterCount = MarketRegimeClusterCatalog.RequiredClusterCount,
                MinTrainingSamples = 50,
            };
            var modelPath = Path.Combine(directory, settings.RegimeModelFileName);
            var manifestPath = modelPath + ".manifest.json";
            await File.WriteAllTextAsync(modelPath, "legacy model without meaning evidence");
            CreateClassifier(settings).IsModelLoaded.Should().BeFalse();

            var classifier = CreateClassifier(settings);
            (await classifier.TrainAsync(Bars(360))).Should().BeTrue();
            classifier.IsModelLoaded.Should().BeTrue();
            var trainedStatus = classifier.GetStatus();
            trainedStatus.TrainingSamples.Should().Be(335);
            trainedStatus.ClusterLabels.Values.Should()
                .BeEquivalentTo(MarketRegimeClusterCatalog.Labels);
            File.Exists(manifestPath).Should().BeTrue();

            var reloaded = CreateClassifier(settings);
            reloaded.IsModelLoaded.Should().BeTrue();
            var reloadedStatus = reloaded.GetStatus();
            reloadedStatus.TrainingSamples.Should().Be(trainedStatus.TrainingSamples);
            reloadedStatus.ClusterLabels.Should().BeEquivalentTo(
                trainedStatus.ClusterLabels);

            var predictionBars = Bars(360);
            var baseline = await reloaded.ClassifyAsync(predictionBars);
            var future = Bar(999, new DateTime(2028, 1, 1));
            future.Close = 1_000_000m;
            future.Volume = 100_000_000;
            var withFuture = await reloaded.ClassifyAsync([.. predictionBars, future]);
            baseline.MlClusterId.Should().BeInRange(1, 4);
            baseline.MlRegimeLabel.Should().BeOneOf(
                MarketRegimeClusterCatalog.Labels.ToArray());
            withFuture.MlClusterId.Should().Be(baseline.MlClusterId);
            withFuture.MlRegimeLabel.Should().Be(baseline.MlRegimeLabel);

            var originalManifest = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<MarketRegimeModelManifest>(
                originalManifest)!;
            var invalidLabels = new Dictionary<uint, string>(manifest.ClusterLabels);
            invalidLabels[invalidLabels.Keys.First()] = "검증되지 않은 국면";
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest with { ClusterLabels = invalidLabels }));
            CreateClassifier(settings).IsModelLoaded.Should().BeFalse(
                "cluster meanings are executable model semantics");

            await File.WriteAllTextAsync(manifestPath, originalManifest);
            await File.AppendAllTextAsync(modelPath, "tampered");
            CreateClassifier(settings).IsModelLoaded.Should().BeFalse(
                "the manifest hash must bind the exact model bytes");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static MarketRegimeClassifier CreateClassifier(MLSettings settings) => new(
        Options.Create(settings),
        new FixedTimeProvider(new DateTimeOffset(2027, 12, 31, 0, 0, 0, TimeSpan.Zero)),
        NullLogger<MarketRegimeClassifier>.Instance);

    private static OhlcvBar[] Bars(int count)
    {
        var price = 100m;
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var section = index / 90 % 4;
                var change = section switch
                {
                    0 => 0.8m,
                    1 => -0.7m,
                    2 => index % 2 == 0 ? 0.15m : -0.15m,
                    _ => index % 2 == 0 ? 3m : -2.5m,
                };
                price = Math.Max(10m, price + change);
                var bar = Bar(index, new DateTime(2026, 1, 1).AddDays(index));
                bar.Open = price - change / 2m;
                bar.High = price + Math.Abs(change) + 1m;
                bar.Low = price - Math.Abs(change) - 1m;
                bar.Close = price;
                bar.Volume = 1_000 + section * 5_000 + index % 20 * 100;
                return bar;
            })
            .ToArray();
    }

    private static OhlcvBar Bar(int index, DateTime timestamp) => new()
    {
        Symbol = "SPY",
        Timestamp = timestamp,
        Open = 100m + index,
        High = 101m + index,
        Low = 99m + index,
        Close = 100.5m + index,
        Volume = 1_000 + index,
        TimeFrame = TimeFrame.Daily,
    };

    private static OhlcvBar Clone(OhlcvBar source) => new()
    {
        Symbol = source.Symbol,
        Timestamp = source.Timestamp,
        Open = source.Open,
        High = source.High,
        Low = source.Low,
        Close = source.Close,
        Volume = source.Volume,
        TimeFrame = source.TimeFrame,
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
