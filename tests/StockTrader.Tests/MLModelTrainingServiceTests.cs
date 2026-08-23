using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.DataFeed;
using StockTrader.Services.ML;
using StockTrader.ServiceContracts.MachineLearning;

namespace StockTrader.Tests;

public class MLModelTrainingServiceTests
{
    [Fact]
    public async Task TrainAllAsync_UsesSelectedProviderBenchmarkClockAndConfiguredMinimum()
    {
        var observedAt = new DateTimeOffset(2026, 8, 18, 6, 0, 0, TimeSpan.Zero);
        var feed = new Mock<IDataFeedService>();
        feed.Setup(value => value.GetHistoricalBarsAsync(
                DataProviderCatalog.KoreaRegimeBenchmark,
                TimeFrame.Daily,
                observedAt.UtcDateTime.AddDays(-90),
                observedAt.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new OhlcvBar { Symbol = DataProviderCatalog.KoreaRegimeBenchmark },
                new OhlcvBar { Symbol = DataProviderCatalog.KoreaRegimeBenchmark }
            ]);
        var feeds = new Mock<IDataFeedServiceFactory>();
        feeds.Setup(value => value.SelectAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataFeedSelection(DataSource.LsSecurities, feed.Object));
        var samples = new Mock<ISignalScoringTrainingStore>();
        samples.Setup(value => value.GetRecentAsync(5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SignalScoringTrainingSample>());
        var transport = new Mock<IMlTrainingTransport>();
        transport.Setup(value => value.TrainAsync(
                It.Is<MarketRegimeTrainingSet>(set =>
                    set.Symbol == DataProviderCatalog.KoreaRegimeBenchmark
                    && set.Provider == nameof(DataSource.LsSecurities)),
                It.IsAny<IReadOnlyList<SignalScoringTrainingSample>>(),
                It.Is<MlTrainingOptions>(options => options.MinimumTrainingSamples == 3),
                observedAt.UtcDateTime,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Insufficient(observedAt.UtcDateTime));
        var service = new MLModelTrainingService(
            new Mock<IMarketRegimeClassifier>().Object,
            new Mock<ISignalScorer>().Object,
            new MarketRegimeTrainingDataSource(feeds.Object),
            samples.Object,
            transport.Object,
            new MlTrainingRunState(),
            new MlTrainingOptions(3, 90, 5000),
            new FixedTimeProvider(observedAt),
            NullLogger<MLModelTrainingService>.Instance);

        var result = await service.TrainAllAsync();

        result.Success.Should().BeFalse();
        result.RegimeSamples.Should().Be(0);
        result.TrainingDuration.Should().Be(TimeSpan.Zero);
        result.Message.Should().Contain("현재 인과적 샘플 0개");
        feed.VerifyAll();
        transport.VerifyAll();
    }

    [Fact]
    public void StatusQueryProjectsOneApplicationOwnedModelSnapshot()
    {
        var regime = new Mock<IMarketRegimeClassifier>();
        regime.SetupGet(value => value.IsModelLoaded).Returns(true);
        regime.Setup(value => value.GetStatus()).Returns(
            new MarketRegimeClassifierStatus(
                true,
                new DateTime(2026, 8, 19, 1, 0, 0, DateTimeKind.Utc),
                250,
                new Dictionary<uint, string> { [1] = "강세장" }));
        var scorer = new Mock<ISignalScorer>();
        scorer.SetupGet(value => value.IsModelLoaded).Returns(true);
        scorer.Setup(value => value.GetStatus()).Returns(
            new SignalScorerStatus(
                true,
                null,
                120,
                0.75,
                0.82,
                [new FeatureImportance("RSI", 0.4)]));
        var query = new MlModelStatusQuery(
            regime.Object,
            scorer.Object,
            new MlTrainingRunState());

        var status = query.GetStatus();

        status.RegimeClusterLabels.Should().Contain(1, "강세장");
        status.SignalScorerAccuracy.Should().Be(0.75);
        status.SignalScorerAuc.Should().Be(0.82);
        status.SignalScorerFeatureImportances.Should()
            .ContainSingle(value => value.FeatureName == "RSI" && value.Importance == 0.4);
    }

    [Fact]
    public async Task ScopedUseCasesShareOneGlobalTrainingClaim()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var regimeData = new Mock<IMarketRegimeTrainingDataSource>();
        regimeData.Setup(value => value.LoadAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.SetResult();
                await release.Task;
                return new MarketRegimeTrainingSet("SPY", []);
            });
        var store = new Mock<ISignalScoringTrainingStore>();
        store.Setup(value => value.GetRecentAsync(5000, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var runState = new MlTrainingRunState();
        var transport = new Mock<IMlTrainingTransport>();
        transport.Setup(value => value.TrainAsync(
                It.IsAny<MarketRegimeTrainingSet>(),
                It.IsAny<IReadOnlyList<SignalScoringTrainingSample>>(),
                It.IsAny<MlTrainingOptions>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MarketRegimeTrainingSet _,
                IReadOnlyList<SignalScoringTrainingSample> _, MlTrainingOptions _,
                DateTime requested, CancellationToken _) => Insufficient(requested));
        MLModelTrainingService CreateService() => new(
            Mock.Of<IMarketRegimeClassifier>(),
            Mock.Of<ISignalScorer>(),
            regimeData.Object,
            store.Object,
            transport.Object,
            runState,
            new MlTrainingOptions(3, 90, 5000),
            TimeProvider.System,
            NullLogger<MLModelTrainingService>.Instance);

        var first = CreateService().TrainAllAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = await CreateService().TrainAllAsync();

        second.Success.Should().BeFalse();
        second.Message.Should().Contain("이미 학습이 진행 중");
        runState.Snapshot().IsTraining.Should().BeTrue();

        release.SetResult();
        await first;
        runState.Snapshot().IsTraining.Should().BeFalse();
    }

    private static MlTrainingJobResult Insufficient(DateTime at) => new(
        MlTrainingContractVersions.Current, "test-job", "test-hash",
        MlTrainingJobStatuses.InsufficientData, "insufficient", 0,
        at, at, at, 0, null, null, false);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
