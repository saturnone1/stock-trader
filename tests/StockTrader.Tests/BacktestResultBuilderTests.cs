using FluentAssertions;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class BacktestResultBuilderTests
{
    [Fact]
    public void Build_PreservesPortfolioTotalsAndTradeStatistics()
    {
        var result = BacktestResultBuilder.Build(new BacktestResultInputs
        {
            Symbols = ["AAA", "BBB"],
            Trades =
            [
                Trade("AAA", new DateTime(2025, 1, 2), pnl: 100m, pnlPercent: 0.10m),
                Trade("BBB", new DateTime(2025, 1, 3), pnl: -40m, pnlPercent: -0.04m)
            ],
            RegimeByDate = [],
            EquityCurve = [new EquityPoint(new DateTime(2025, 1, 1), 1_000m)],
            Warnings = [],
            From = new DateTime(2025, 1, 1),
            To = new DateTime(2025, 1, 31),
            TimeFrame = TimeFrame.Daily,
            InitialCapital = 1_000m,
            CurrentEquity = 1_060m,
            MaxDrawdown = 0.04m,
            TotalSlippage = 2.5m,
            TotalCommission = 2m,
            WeightStrategyApplied = true,
            WeightReducedTrades = 1
        });

        result.TotalReturn.Should().Be(60m);
        result.TotalReturnPercent.Should().Be(0.06m);
        result.TotalTrades.Should().Be(2);
        result.OverallWinRate.Should().Be(0.5m);
        result.TotalSlippageCost.Should().Be(2.5m);
        result.TotalCommissionCost.Should().Be(2m);
        result.WeightStrategyApplied.Should().BeTrue();
        result.WeightReducedTrades.Should().Be(1);
    }

    [Fact]
    public void Build_WarnsForConcentratedLongTermLeveragedEtfTest()
    {
        var input = EmptyInput() with
        {
            Symbols = ["TQQQ"],
            From = new DateTime(2020, 1, 1),
            To = new DateTime(2025, 1, 1)
        };

        var result = BacktestResultBuilder.Build(input);

        result.SurvivorshipBiasWarning.Should().Contain("생존자 편향");
    }

    private static BacktestResultInputs EmptyInput() => new()
    {
        Symbols = [],
        Trades = [],
        RegimeByDate = [],
        EquityCurve = [],
        Warnings = [],
        From = new DateTime(2025, 1, 1),
        To = new DateTime(2025, 1, 2),
        TimeFrame = TimeFrame.Daily,
        InitialCapital = 1_000m,
        CurrentEquity = 1_000m,
        MaxDrawdown = 0m,
        TotalSlippage = 0m,
        TotalCommission = 0m,
        WeightStrategyApplied = false,
        WeightReducedTrades = 0
    };

    private static TradeRecord Trade(
        string symbol,
        DateTime entry,
        decimal pnl,
        decimal pnlPercent) => new()
    {
        Symbol = symbol,
        EntryTime = entry,
        ExitTime = entry.AddDays(1),
        EntryPrice = 100m,
        ExitPrice = 100m + pnl / 10m,
        Quantity = 10,
        PnL = pnl,
        PnLPercent = pnlPercent
    };
}
