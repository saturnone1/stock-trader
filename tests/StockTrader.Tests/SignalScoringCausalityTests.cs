using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.ML;

namespace StockTrader.Tests;

public sealed class SignalScoringCausalityTests
{
    [Fact]
    public void FeatureFactoryUsesPlannedRiskAndIgnoresBarsAfterSignalTime()
    {
        var bars = Bars(220);
        var signal = new PatternSignal
        {
            PatternType = PatternType.Breakout,
            EntryPrice = 100m,
            StopLossPrice = 95m,
            TargetPrice = 115m,
            SignalBarAt = bars[^1].Timestamp,
            Confidence = 0.6m,
        };
        var future = new OhlcvBar
        {
            Timestamp = bars[^1].Timestamp.AddDays(1),
            Open = 10_000m,
            High = 11_000m,
            Low = 9_000m,
            Close = 10_500m,
            Volume = 9_000_000,
            TimeFrame = TimeFrame.Daily,
        };
        var sut = new SignalScoringFeatureFactory(new IndicatorService());

        var baseline = sut.Create(signal, bars, Regime("강세장"), 0.62m);
        var withFuture = sut.Create(signal, [.. bars, future], Regime("강세장"), 0.62m);

        baseline.Should().NotBeNull();
        withFuture.Should().Be(baseline);
        baseline!.RiskRewardRatio.Should().Be(3f);
        baseline.HistoricalWinRate.Should().BeApproximately(0.62f, 0.0001f);
        baseline.LongTrendHistoryAvailable.Should().Be(1f);
        baseline.SchemaVersion.Should().Be(SignalScoringFeatureSchema.CurrentVersion);
    }

    [Fact]
    public async Task TrainingStoreGroupsPartialExitsAndExcludesLegacyFeaturelessRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.PatternSignals.AddRange(
                StoredSignal(101, new DateTime(2026, 8, 1), complete: true),
                StoredSignal(102, new DateTime(2026, 8, 2), complete: true),
                StoredSignal(103, new DateTime(2026, 8, 3), complete: false));
            seed.TradeRecords.AddRange(
                Trade(101, 20m),
                Trade(101, -5m),
                Trade(102, -2m),
                Trade(103, 100m));
            await seed.SaveChangesAsync();
        }

        var samples = await new SignalScoringTrainingStore(
                new TestDbContextFactory(options))
            .GetRecentAsync(100);

        samples.Should().HaveCount(2);
        samples.Select(sample => sample.SourceSignalId).Should().Equal(101, 102);
        samples[0].IsWin.Should().BeTrue("partial exits sum to one original decision outcome");
        samples[1].IsWin.Should().BeFalse();
        samples.Should().OnlyContain(sample => sample.Features.RiskRewardRatio == 2f);
    }

    [Fact]
    public void DatasetSplitUsesOldestForTrainingAndNewestForValidation()
    {
        var samples = Enumerable.Range(1, 20)
            .Select(index => Sample(index, index % 2 == 0))
            .Reverse()
            .ToArray();

        var accepted = SignalScoringDatasetPolicy.TrySplit(
            samples, out var split, out var reason);

        accepted.Should().BeTrue(reason);
        split!.Training.Should().HaveCount(16);
        split.Validation.Should().HaveCount(4);
        split.Training.Max(sample => sample.SignalBarAt)
            .Should().BeBefore(split.Validation.Min(sample => sample.SignalBarAt));
    }

    [Fact]
    public async Task ScorerRejectsLegacyArtifactAndPersistsVerifiableCurrentModel()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), $"stocktrader-signal-model-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new MLSettings
            {
                ModelDirectory = directory,
                SignalScorerModelFileName = "signal.zip",
                MinTrainingSamples = 50,
                MlScoreBlendWeight = 0.5,
                EnableMlScoring = true,
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, settings.SignalScorerModelFileName),
                "legacy artifact without a manifest");
            var legacy = CreateScorer(settings);
            legacy.IsModelLoaded.Should().BeFalse();

            var trained = await legacy.TrainAsync(
                Enumerable.Range(1, 100)
                    .Select(index => Sample(index, index % 2 == 0))
                    .ToArray());

            trained.Should().BeTrue();
            legacy.IsModelLoaded.Should().BeTrue();
            var trainedStatus = legacy.GetStatus();
            trainedStatus.TrainingSamples.Should().Be(100);
            trainedStatus.FeatureImportances.Should().HaveCount(
                SignalScoringFeatureSchema.FeatureCount);
            File.Exists(Path.Combine(directory, "signal.zip.manifest.json"))
                .Should().BeTrue();

            var reloaded = CreateScorer(settings);
            reloaded.IsModelLoaded.Should().BeTrue();
            var reloadedStatus = reloaded.GetStatus();
            reloadedStatus.TrainingSamples.Should().Be(100);
            reloadedStatus.ValidationAccuracy.Should().Be(
                trainedStatus.ValidationAccuracy);
            reloadedStatus.ValidationAuc.Should().Be(trainedStatus.ValidationAuc);

            await File.AppendAllTextAsync(Path.Combine(directory, "signal.zip"), "tampered");
            CreateScorer(settings).IsModelLoaded.Should().BeFalse(
                "the manifest hash must bind the exact executable artifact");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SignalScorer CreateScorer(MLSettings settings) => new(
        Options.Create(settings),
        new FixedTimeProvider(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero)),
        new IndicatorService(),
        NullLogger<SignalScorer>.Instance);

    private static SignalScoringTrainingSample Sample(int index, bool isWin) => new(
        index,
        new DateTime(2026, 1, 1).AddDays(index),
        new SignalScoringFeatures(
            SignalScoringFeatureSchema.CurrentVersion,
            index % 4,
            isWin ? 0.7f : 0.3f,
            index / 100f,
            1f + index / 100f,
            index % 4,
            0.01f + index / 10_000f,
            0.5f,
            2f,
            (index - 50) / 100f,
            1f),
        isWin);

    private static PatternSignal StoredSignal(long id, DateTime at, bool complete) => new()
    {
        Id = id,
        Symbol = $"SYM{id}",
        PatternType = PatternType.Breakout,
        SignalBarAt = at,
        DetectedAt = at,
        EntryPrice = 100m,
        StopLossPrice = 95m,
        TargetPrice = 110m,
        IsActive = true,
        ScoringFeatureVersion = complete ? SignalScoringFeatureSchema.CurrentVersion : null,
        ScoringRsi = complete ? 0.5f : null,
        ScoringBollingerPosition = complete ? 0.5f : null,
        ScoringVolumeRatio = complete ? 1f : null,
        ScoringMarketRegimeCode = complete ? 0f : null,
        ScoringAtrPercent = complete ? 0.02f : null,
        ScoringHistoricalWinRate = complete ? 0.5f : null,
        ScoringRiskRewardRatio = complete ? 2f : null,
        ScoringPriceVsLongMovingAverage = complete ? 0.1f : null,
        ScoringLongTrendHistoryAvailable = complete ? 1f : null,
    };

    private static TradeRecord Trade(long sourceSignalId, decimal pnl) => new()
    {
        SourceSignalId = sourceSignalId,
        Symbol = $"SYM{sourceSignalId}",
        PatternType = PatternType.Breakout,
        EntryPrice = 100m,
        ExitPrice = 101m,
        Quantity = 1,
        EntryTime = new DateTime(2026, 8, 1),
        ExitTime = new DateTime(2026, 8, 2),
        PnL = pnl,
        PnLPercent = pnl / 100m,
    };

    private static OhlcvBar[] Bars(int count) => Enumerable.Range(0, count)
        .Select(index => new OhlcvBar
        {
            Timestamp = new DateTime(2025, 1, 1).AddDays(index),
            Open = 100m + index / 10m,
            High = 101m + index / 10m,
            Low = 99m + index / 10m,
            Close = 100.5m + index / 10m,
            Volume = 1_000 + index,
            TimeFrame = TimeFrame.Daily,
        })
        .ToArray();

    private static MarketRegime Regime(string label) => new()
    {
        RegimeLabel = label,
        MlClusterId = -1,
    };

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
