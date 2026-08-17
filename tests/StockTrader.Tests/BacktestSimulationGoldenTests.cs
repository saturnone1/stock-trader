using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Application.Backtesting;
using StockTrader.Configuration;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class BacktestSimulationGoldenTests
{
    [Fact]
    public async Task RunAsync_NextOpenRepricesRiskAndEvaluatesTheEntryBar()
    {
        var bars = Bars(TimeFrame.Daily, TimeSpan.FromDays(1));
        bars[51].Open = 105m;
        bars[51].High = 110m;
        bars[51].Low = 104m;
        bars[51].Close = 109m;
        var entryAt = bars[50].Timestamp;
        var definition = new CustomPatternDefinition
        {
            Name = "next-open-golden",
            EntryMode = StrategyCatalog.NextOpenEntryMode,
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
            AtrStopMultiplier = 1m,
            AtrTargetMultiplier = 2m,
            MaxHoldingBars = 10
        };
        var detector = new RuleBasedDetector(new IndicatorService(), definition);
        var engine = CreateEngine();

        var result = await engine.RunAsync(
            ["AAA"],
            new Dictionary<string, PreparedSymbolData> { ["AAA"] = Prepared(bars) },
            [detector],
            [],
            entryAt,
            bars[^1].Timestamp,
            100_000m,
            0m,
            0m,
            TimeFrame.Daily,
            new BacktestRiskParameters(0.01m, 0.03m, 10, 2),
            null,
            SlippageModel.Fixed,
            [],
            bars[0].Timestamp,
            new BacktestExecutionAdapter(),
            null,
            new CumulativeRsi2Config(),
            CancellationToken.None);

        result.Trades.Should().ContainSingle();
        var trade = result.Trades[0];
        trade.EntryPrice.Should().Be(105m);
        trade.ExitPrice.Should().Be(109m);
        trade.Quantity.Should().Be(95);
        trade.ExitReason.Should().Be("목표 도달");
        result.TotalReturn.Should().Be(380m);
    }

    [Theory]
    [InlineData(TimeFrame.OneMinute)]
    [InlineData(TimeFrame.Daily)]
    [InlineData(TimeFrame.Weekly)]
    public async Task RunAsync_PreservesGoldenEntryExitAndReturnAcrossTimeFrames(TimeFrame timeFrame)
    {
        var interval = timeFrame switch
        {
            TimeFrame.OneMinute => TimeSpan.FromMinutes(1),
            TimeFrame.Weekly => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(1)
        };
        var bars = Bars(timeFrame, interval);
        var entryAt = bars[50].Timestamp;
        var result = await RunBaselineAsync(
            bars, timeFrame, new SingleEntryDetector(entryAt));

        result.TotalTrades.Should().Be(1);
        result.TotalReturn.Should().Be(1_000m);
        result.TotalReturnPercent.Should().Be(0.01m);
        result.Trades.Should().ContainSingle(trade =>
            trade.EntryPrice == 100m
            && trade.ExitPrice == 110m
            && trade.Quantity == 100
            && trade.ExitReason == "목표 도달");
    }

    [Fact]
    public async Task RunAsync_StopWinsWhenStopAndTargetAreBothTouchedInOneBar()
    {
        var bars = Bars(TimeFrame.Daily, TimeSpan.FromDays(1));
        bars[51].Low = 94m;
        var result = await RunBaselineAsync(
            bars,
            TimeFrame.Daily,
            new SingleEntryDetector(bars[50].Timestamp));

        result.Trades.Should().ContainSingle(trade =>
            trade.ExitPrice == 95m
            && trade.ExitReason == "손절"
            && trade.Quantity == 100);
        result.TotalReturn.Should().Be(-500m);
    }

    [Fact]
    public async Task RunAsync_PartialProfitPrecedesFinalLiquidationWithoutDoubleCounting()
    {
        var bars = Bars(TimeFrame.Daily, TimeSpan.FromDays(1));
        var result = await RunBaselineAsync(
            bars,
            TimeFrame.Daily,
            new SingleEntryDetector(
                bars[50].Timestamp,
                PatternType.GapUpPullback,
                targetPrice: 120m));

        result.Trades.Should().HaveCount(2);
        result.Trades.Should().ContainSingle(trade =>
            trade.ExitReason == "부분 익절(2.0R)"
            && trade.Quantity == 50
            && trade.ExitPrice == 110m);
        result.Trades.Should().ContainSingle(trade =>
            trade.ExitReason == "기간 종료"
            && trade.Quantity == 50);
        result.TotalReturn.Should().Be(1_000m);
        result.TotalTrades.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_BearRegimeReducesQuantityThroughSharedAllocationPolicy()
    {
        var bars = Bars(TimeFrame.Daily, TimeSpan.FromDays(1));
        var entryAt = bars[50].Timestamp;
        var regimes = new Dictionary<DateOnly, MarketRegime>
        {
            [DateOnly.FromDateTime(entryAt)] = new()
            {
                SpyAbove200Ma = false,
                SpyPrice = 90m,
                Spy200Ma = 100m
            }
        };
        var weightStrategy = new WeightStrategy { BearWeight = 0.5m };

        var result = await RunBaselineAsync(
            bars,
            TimeFrame.Daily,
            new SingleEntryDetector(entryAt),
            regimes,
            weightStrategy);

        result.Trades.Should().ContainSingle(trade => trade.Quantity == 50);
        result.TotalReturn.Should().Be(500m);
        result.WeightReducedTrades.Should().Be(1);
        result.WeightStrategyApplied.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_HighCorrelationBlocksSecondSymbol()
    {
        var firstBars = CorrelatedBars("FIRST");
        var secondBars = CorrelatedBars("SECOND");
        var entryAt = firstBars[50].Timestamp;
        var definition = new CustomPatternDefinition
        {
            Name = "correlation-golden",
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_CHANGE",
                    Operator = ">",
                    Value = -100m,
                    Params = new Dictionary<string, decimal> { ["bars"] = 1m }
                }
            }),
            AtrStopMultiplier = 2m,
            AtrTargetMultiplier = 10m,
            MaxHoldingBars = 100,
            PortfolioRulesJson = JsonSerializer.Serialize(new PortfolioRulesConfig
            {
                MaxCorrelation = 0.8m
            })
        };
        var detector = new RuleBasedDetector(new IndicatorService(), definition);

        var result = await CreateEngine()
            .RunAsync(
                ["FIRST", "SECOND"],
                new Dictionary<string, PreparedSymbolData>
                {
                    ["FIRST"] = Prepared(firstBars),
                    ["SECOND"] = Prepared(secondBars)
                },
                [detector],
                [],
                entryAt,
                firstBars[^1].Timestamp,
                100_000m,
                0m,
                0m,
                TimeFrame.Daily,
                new BacktestRiskParameters(0.01m, 0.03m, 10, 2),
                null,
                SlippageModel.Fixed,
                [],
                firstBars[0].Timestamp,
                new BacktestExecutionAdapter(),
                null,
                new CumulativeRsi2Config(),
                CancellationToken.None);

        result.Trades.Should().OnlyContain(trade => trade.Symbol == "FIRST");
        result.TotalTrades.Should().Be(1);
    }

    private static OhlcvBar[] Bars(TimeFrame timeFrame, TimeSpan interval)
    {
        var start = new DateTime(2024, 1, 1, 9, 30, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, 52).Select(index =>
        {
            var isExitBar = index == 51;
            return new OhlcvBar
            {
                Symbol = "AAA",
                TimeFrame = timeFrame,
                Timestamp = start.AddTicks(interval.Ticks * index),
                Open = 100m,
                High = isExitBar ? 111m : 101m,
                Low = 99m,
                Close = isExitBar ? 110m : 100m,
                Volume = 1_000_000
            };
        }).ToArray();
    }

    private static OhlcvBar[] CorrelatedBars(string symbol)
    {
        var close = 100m;
        return Enumerable.Range(0, 52).Select(index =>
        {
            close *= 1m + (index % 5 - 2) / 1_000m;
            return new OhlcvBar
            {
                Symbol = symbol,
                TimeFrame = TimeFrame.Daily,
                Timestamp = new DateTime(2024, 1, 1).AddDays(index),
                Open = close,
                High = close + 0.5m,
                Low = close - 0.5m,
                Close = close,
                Volume = 1_000_000
            };
        }).ToArray();
    }

    private static PreparedSymbolData Prepared(OhlcvBar[] bars)
    {
        var closes = bars.Select(bar => bar.Close).ToArray();
        return new PreparedSymbolData(
            bars,
            Enumerable.Repeat(5m, bars.Length).ToArray(),
            closes,
            Enumerable.Repeat(90m, bars.Length).ToArray(),
            new decimal[bars.Length],
            new decimal[bars.Length],
            bars.Select((bar, index) => (bar.Timestamp, index))
                .ToDictionary(pair => pair.Timestamp, pair => pair.index));
    }

    private static Task<BacktestResult> RunBaselineAsync(
        OhlcvBar[] bars,
        TimeFrame timeFrame,
        IPatternDetector detector,
        Dictionary<DateOnly, MarketRegime>? regimes = null,
        WeightStrategy? weightStrategy = null)
    {
        var entryAt = bars[50].Timestamp;
        return CreateEngine()
            .RunAsync(
                ["AAA"],
                new Dictionary<string, PreparedSymbolData> { ["AAA"] = Prepared(bars) },
                [detector],
                regimes ?? [],
                entryAt,
                bars[^1].Timestamp,
                100_000m,
                0m,
                0m,
                timeFrame,
                new BacktestRiskParameters(0.01m, 0.03m, 10, 2),
                null,
                SlippageModel.Fixed,
                [],
                bars[0].Timestamp,
                new BacktestExecutionAdapter(),
                weightStrategy,
                new CumulativeRsi2Config(),
                CancellationToken.None);
    }

    private static BacktestSimulationEngine CreateEngine() => new(
        new BacktestSignalEntryProcessor(
            NullLogger<BacktestSignalEntryProcessor>.Instance));

    private sealed class SingleEntryDetector(
        DateTime entryAt,
        PatternType patternType = PatternType.Breakout,
        decimal targetPrice = 110m) : IPatternDetector
    {
        public PatternType PatternType => patternType;

        public Task<PatternSignal?> DetectAsync(
            string symbol,
            OhlcvBar[] bars,
            MarketRegime regime,
            CancellationToken ct = default)
        {
            PatternSignal? signal = bars[^1].Timestamp == entryAt
                ? new PatternSignal
                {
                    Symbol = symbol,
                    PatternType = PatternType,
                    EntryPrice = 100m,
                    StopLossPrice = 95m,
                    TargetPrice = targetPrice,
                    AllocationScale = 1m
                }
                : null;
            return Task.FromResult(signal);
        }
    }
}
