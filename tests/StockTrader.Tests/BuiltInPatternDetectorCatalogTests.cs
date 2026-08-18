using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Extensions;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public sealed class BuiltInPatternDetectorCatalogTests
{
    [Fact]
    public void Catalog_CoversEveryOperationalBuiltInPatternExactlyOnce()
    {
        var expected = PatternCatalog.OperationalBuiltIn
            .Select(item => item.Value)
            .Order()
            .ToArray();

        BuiltInPatternDetectorCatalog.All.Select(item => item.PatternType)
            .Should().BeEquivalentTo(expected);
        BuiltInPatternDetectorCatalog.All.Select(item => item.PatternType)
            .Should().OnlyHaveUniqueItems();
        BuiltInPatternDetectorCatalog.All.Select(item => item.ImplementationType)
            .Should().OnlyHaveUniqueItems();
        BuiltInPatternDetectorCatalog.All.Select(item => item.PatternType).Should().NotContain([
            PatternType.OpeningRangeBreakout,
            PatternType.EarningsDrift]);
    }

    [Theory]
    [InlineData(PatternType.OpeningRangeBreakout)]
    [InlineData(PatternType.EarningsDrift)]
    public void Factory_RejectsCatalogEntriesWithoutOperationalSemantics(PatternType patternType)
    {
        var services = new ServiceCollection()
            .AddSingleton<IIndicatorService, IndicatorService>()
            .BuildServiceProvider();
        var factory = new BuiltInPatternDetectorFactory(services);

        var action = () => factory.Create(patternType, new PatternSettings());

        action.Should().Throw<NotSupportedException>()
            .WithMessage("*구현되지 않았습니다*");
    }

    [Fact]
    public void Factory_RejectsUnknownPatternCodesWithoutASequenceException()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var factory = new BuiltInPatternDetectorFactory(services);

        var action = () => factory.Create((PatternType)999, new PatternSettings());

        action.Should().Throw<NotSupportedException>()
            .WithMessage("*알 수 없는 전략 코드(999)*");
    }

    [Fact]
    public void Factory_CreatesEveryCatalogDetectorIncludingTqqq()
    {
        var services = new ServiceCollection()
            .AddSingleton<IIndicatorService, IndicatorService>()
            .AddSingleton(new Mock<ISettingsRepository>().Object)
            .BuildServiceProvider();
        var factory = new BuiltInPatternDetectorFactory(services);

        var detectors = factory.CreateAll(new PatternSettings());

        detectors.Select(detector => detector.PatternType)
            .Should().BeEquivalentTo(BuiltInPatternDetectorCatalog.All.Select(item => item.PatternType));
        detectors.Should().ContainSingle(detector => detector is Tqqq200SmaDetector);
    }

    [Fact]
    public void AddPatternServices_ResolvesTheCatalogAsTheRuntimeInventory()
    {
        var services = new ServiceCollection();
        services.AddOptions().Configure<PatternSettings>(_ => { });
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => new Mock<ISettingsRepository>().Object);
        services.AddPatternServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var detectors = scope.ServiceProvider.GetServices<IPatternDetector>().ToArray();

        detectors.Select(detector => detector.PatternType)
            .Should().BeEquivalentTo(BuiltInPatternDetectorCatalog.All.Select(item => item.PatternType));
    }
}
