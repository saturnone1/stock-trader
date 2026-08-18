using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Domain.Backtesting;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class BacktestPositionExitProcessorTests
{
    [Fact]
    public void Process_ScaleOutStateSurvivesARecreatedProcessor()
    {
        var bars = Enumerable.Range(0, 60).Select(index => new OhlcvBar
        {
            Symbol = "AAA",
            Timestamp = new DateTime(2025, 1, 1).AddDays(index),
            Open = index == 59 ? 110m : 100m,
            High = index == 59 ? 111m : 101m,
            Low = index == 59 ? 109m : 99m,
            Close = index == 59 ? 110m : 100m,
            Volume = 1_000_000
        }).ToArray();
        var definition = new StrategyDocument
        {
            Name = "scale-out-test",
            EntryRulesJson = JsonSerializer.Serialize(new[] { PassingRule() }),
            ScalingRulesJson = JsonSerializer.Serialize(new[]
            {
                new ScalingRule
                {
                    Direction = StrategyCatalog.ScalingOutDirection,
                    Percent = 20m,
                    MaxCount = 1,
                    Conditions = [PassingRule()]
                }
            })
        };
        var detector = new RuleBasedDetector(new IndicatorService(), definition, TimeProvider.System);
        var portfolio = new BacktestPortfolioState(100_000m, bars[0].Timestamp);
        portfolio.OpenPositions["AAA"] = new BacktestExecutionAdapter.OpenPosition
        {
            PatternType = PatternType.Custom,
            CustomPatternName = definition.Name,
            EntryPrice = 100m,
            OriginalStop = 50m,
            StopLoss = 50m,
            Target = 200m,
            Quantity = 5,
            InitialQuantity = 10,
            CurrentQuantity = 5,
            TotalCost = 500m,
            EntryTime = bars[0].Timestamp,
            EntryBarIndex = 0,
            EntryAtr = 5m,
            HighestHighSinceEntry = 100m,
            LowestLowSinceEntry = 100m,
            RiskDistance = 50m,
            CustomExitProfile = new LongPositionExitPolicy(
                999, false, 0m, 0m, false, 0m, false, false)
        };
        var prepared = Prepared(bars);
        var symbolData = new Dictionary<string, PreparedSymbolData> { ["AAA"] = prepared };
        var runtimeRegistry = new BacktestStrategyRuntimeRegistry(
            [detector], symbolData, 100_000m);
        var tradeLedger = new BacktestTradeLedger(
            portfolio, runtimeRegistry, SlippageModel.Fixed, 0m, 0m);

        var context = new BacktestPositionExitContext(
            bars[^1].Timestamp,
            59,
            symbolData,
            260,
            10,
            new CumulativeRsi2Config(),
            [],
            null,
            portfolio,
            runtimeRegistry,
            tradeLedger,
            new BacktestExecutionAdapter());

        new BacktestPositionExitProcessor().Process(context);

        tradeLedger.Trades.Should().ContainSingle(trade =>
            trade.ExitReason == "분할 매도(20%)"
            && trade.Quantity == 2
            && trade.ExitPrice == 110m);
        portfolio.OpenPositions["AAA"].CurrentQuantity.Should().Be(3);
        portfolio.OpenPositions["AAA"].TotalCost.Should().Be(300m);
        portfolio.OpenPositions["AAA"].ScaleCounts.Should().ContainKey(0).WhoseValue.Should().Be(1);

        new BacktestPositionExitProcessor().Process(context);

        tradeLedger.Trades.Should().ContainSingle(
            "the filled rule count belongs to the position, not the processor instance");
        portfolio.OpenPositions["AAA"].CurrentQuantity.Should().Be(3);
    }

    private static EntryRule PassingRule() => new()
    {
        Indicator = "PRICE_CHANGE",
        Operator = ">=",
        Value = 0m,
        Params = new Dictionary<string, decimal> { ["bars"] = 1m }
    };

    private static PreparedSymbolData Prepared(OhlcvBar[] bars) => new(
        bars,
        Enumerable.Repeat(5m, bars.Length).ToArray(),
        bars.Select(bar => bar.Close).ToArray(),
        Enumerable.Repeat(90m, bars.Length).ToArray(),
        new decimal[bars.Length],
        new decimal[bars.Length],
        bars.Select((bar, index) => (bar.Timestamp, index))
            .ToDictionary(pair => pair.Timestamp, pair => pair.index));
}
