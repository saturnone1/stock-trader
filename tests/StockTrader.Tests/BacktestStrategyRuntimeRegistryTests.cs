using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class BacktestStrategyRuntimeRegistryTests
{
    [Fact]
    public void RuntimeRegistry_OwnsDailyEntryAndDrawdownStateTransitions()
    {
        var registry = CreateRegistry(new CircuitBreakerConfig
        {
            MaxDrawdownPercent = 10m
        });
        var day = new DateOnly(2025, 1, 2);

        registry.RegisterEntry("strategy-a", day);
        registry.BeginStep(day.ToDateTime(new TimeOnly(12, 0)), day);
        registry.Find("strategy-a")!.DailyEntryCount.Should().Be(1);

        registry.ApplyRealizedTrade(Trade(10_000m));
        registry.ApplyRealizedTrade(Trade(-11_000m));

        var runtime = registry.Find("strategy-a")!;
        runtime.RealizedEquity.Should().Be(99_000m);
        runtime.PeakEquity.Should().Be(110_000m);
        runtime.CircuitBreakerTripped.Should().BeTrue();

        var nextDay = day.AddDays(1);
        registry.BeginStep(nextDay.ToDateTime(new TimeOnly(12, 0)), nextDay);
        runtime.DailyEntryCount.Should().Be(0);
    }

    [Fact]
    public void RuntimeRegistry_OwnsPerSymbolReentryCooldownKeys()
    {
        var registry = CreateRegistry(
            new CircuitBreakerConfig(),
            new ReentryConfig { CooldownBarsAfterLoss = 2 });

        registry.RegisterClosedTrade(
            "strategy-a", "AAA", currentBarIndex: 10, currentTimelineStep: 5, Trade(-1m));

        registry.GetCooldownUntil("strategy-a", "AAA").Should().Be(13);
        registry.GetCooldownUntil("strategy-a", "BBB").Should().BeNull();
    }

    private static BacktestStrategyRuntimeRegistry CreateRegistry(
        CircuitBreakerConfig circuitBreaker,
        ReentryConfig? reentry = null)
    {
        var definition = new StrategyDocument
        {
            Name = "strategy-a",
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_CHANGE",
                    Operator = ">=",
                    Value = 0m,
                    Params = new Dictionary<string, decimal> { ["bars"] = 1m }
                }
            }),
            CircuitBreakerJson = JsonSerializer.Serialize(circuitBreaker),
            ReentryJson = JsonSerializer.Serialize(reentry ?? new ReentryConfig())
        };
        var detector = new RuleBasedDetector(new IndicatorService(), definition);
        return new BacktestStrategyRuntimeRegistry(
            [detector],
            new Dictionary<string, PreparedSymbolData>(),
            initialCapital: 100_000m);
    }

    private static TradeRecord Trade(decimal pnl) => new()
    {
        CustomPatternName = "strategy-a",
        PnL = pnl
    };
}
