using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.MachineLearning;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.DataFeed;
using StockTrader.Services.ML;

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
        var services = new ServiceCollection()
            .AddScoped(_ => feeds.Object)
            .AddScoped(_ => samples.Object)
            .BuildServiceProvider();
        var service = new MLModelTrainingService(
            new Mock<IMarketRegimeClassifier>().Object,
            new Mock<ISignalScorer>().Object,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new MLSettings
            {
                MinTrainingSamples = 3,
                RegimeTrainingDays = 90
            }),
            new FixedTimeProvider(observedAt),
            NullLogger<MLModelTrainingService>.Instance);

        var result = await service.TrainAllAsync();

        result.Success.Should().BeFalse();
        result.RegimeSamples.Should().Be(0);
        result.TrainingDuration.Should().Be(TimeSpan.Zero);
        result.Message.Should().Contain("최소 3개 인과적 샘플 필요");
        feed.VerifyAll();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
