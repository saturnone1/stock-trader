using FluentAssertions;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class BacktestStrategyTransitionPolicyTests
{
    [Fact]
    public void RegisterClosedTrade_AppliesLossCooldownAndCircuitBreakerAfterLimit()
    {
        var runtime = Runtime();
        var cooldowns = new Dictionary<string, int>();

        BacktestStrategyTransitionPolicy.RegisterClosedTrade(
            "strategy|AAA", 10, 20, new TradeRecord { PnL = -10m }, runtime, cooldowns);

        cooldowns["strategy|AAA"].Should().Be(13);
        runtime.ConsecutiveLosses.Should().Be(1);
        runtime.CircuitBreakerUntilStep.Should().Be(0);

        BacktestStrategyTransitionPolicy.RegisterClosedTrade(
            "strategy|AAA", 20, 30, new TradeRecord { PnL = -5m }, runtime, cooldowns);

        cooldowns["strategy|AAA"].Should().Be(23);
        runtime.ConsecutiveLosses.Should().Be(0);
        runtime.CircuitBreakerUntilStep.Should().Be(34);
    }

    [Fact]
    public void RegisterClosedTrade_WinResetsLossesAndUsesWinCooldown()
    {
        var runtime = Runtime();
        runtime.ConsecutiveLosses = 1;
        var cooldowns = new Dictionary<string, int>();

        BacktestStrategyTransitionPolicy.RegisterClosedTrade(
            "strategy|AAA", 10, 20, new TradeRecord { PnL = 10m }, runtime, cooldowns);

        cooldowns["strategy|AAA"].Should().Be(12);
        runtime.ConsecutiveLosses.Should().Be(0);
    }

    private static BacktestStrategyRuntime Runtime() => new()
    {
        Detector = null!,
        Reentry = new ReentryConfig
        {
            CooldownBarsAfterLoss = 2,
            CooldownBarsAfterWin = 1
        },
        CircuitBreaker = new CircuitBreakerConfig
        {
            ConsecutiveLossLimit = 2,
            CooldownBars = 3
        },
        Portfolio = new PortfolioRulesConfig()
    };
}
