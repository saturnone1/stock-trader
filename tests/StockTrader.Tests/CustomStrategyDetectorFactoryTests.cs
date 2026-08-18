using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;
using StockTrader.Extensions;

namespace StockTrader.Tests;

public class CustomStrategyDetectorFactoryTests
{
    [Fact]
    public async Task Create_FromDefinitionCompilesTheSharedRuntimeWithDeterministicBarTime()
    {
        var factory = new CustomStrategyDetectorFactory(new IndicatorService());
        var bars = Bars();

        var detector = factory.Create(Definition("factory-clock"));
        var signal = await detector.DetectAsync(
            "AAPL",
            bars,
            new MarketRegime { SpyAbove200Ma = true });

        detector.Strategy.Name.Should().Be("factory-clock");
        signal.Should().NotBeNull();
        signal!.DetectedAt.Should().Be(bars[^1].Timestamp);
        signal.SignalBarAt.Should().Be(bars[^1].Timestamp);
    }

    [Fact]
    public void Create_FromCompiledStrategyPreservesTheExactCompiledAggregate()
    {
        var compiled = StrategyCompiler.Compile(Definition("compiled-once")).Strategy!;
        var factory = new CustomStrategyDetectorFactory(new IndicatorService());

        var detector = factory.Create(compiled);

        detector.Strategy.Should().BeSameAs(compiled);
        detector.Definition.Should().BeSameAs(compiled.Source);
    }

    [Fact]
    public void Create_ReturnsAnIsolatedRuntimeForEveryExecutionScope()
    {
        var factory = new CustomStrategyDetectorFactory(new IndicatorService());
        var definition = Definition("isolated-runtime");

        var first = factory.Create(definition);
        var second = factory.Create(definition);

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void Create_InvalidDefinitionCannotBypassCentralCompilation()
    {
        var factory = new CustomStrategyDetectorFactory(new IndicatorService());
        var invalid = Definition("invalid");
        invalid.EntryGroupsJson = "{broken";

        var action = () => factory.Create(invalid);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddPatternServices_ResolvesTheSingleRegisteredFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddSingleton(TimeProvider.System);
        services.AddPatternServices();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<ICustomStrategyDetectorFactory>();
        var second = provider.GetRequiredService<ICustomStrategyDetectorFactory>();

        first.Should().BeOfType<CustomStrategyDetectorFactory>();
        second.Should().BeSameAs(first);
    }

    private static StrategyDocument Definition(string name) => new()
    {
        Name = name,
        EntryRulesJson = JsonSerializer.Serialize(new[]
        {
            new EntryRule { Indicator = "PRICE_CHANGE", Operator = ">", Value = -1m }
        }),
        AtrStopMultiplier = 2m,
        AtrTargetMultiplier = 3m
    };

    private static OhlcvBar[] Bars() => Enumerable.Range(0, 60).Select(index => new OhlcvBar
    {
        Timestamp = new DateTime(2024, 1, 1).AddDays(index),
        Open = 100m,
        High = 101m,
        Low = 99m,
        Close = 100m,
        Volume = 100_000
    }).ToArray();

}
