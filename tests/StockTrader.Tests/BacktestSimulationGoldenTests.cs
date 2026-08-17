using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.Backtesting;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class BacktestSimulationGoldenTests
{
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
        var prepared = Prepared(bars);
        var indicators = new Mock<IIndicatorService>();
        var simulator = new TradeSimulator(
            indicators.Object,
            NullLogger<TradeSimulator>.Instance);
        var engine = new BacktestSimulationEngine(
            NullLogger<BacktestSimulationEngine>.Instance);

        var result = await engine.RunAsync(
            ["AAA"],
            new Dictionary<string, PreparedSymbolData> { ["AAA"] = prepared },
            [new SingleEntryDetector(entryAt)],
            [],
            entryAt,
            bars[^1].Timestamp,
            initialCapital: 100_000m,
            slippagePercent: 0m,
            commissionPerTrade: 0m,
            timeFrame,
            new BacktestRiskParameters(0.01m, 0.03m, 10, 2),
            exitOverrides: null,
            SlippageModel.Fixed,
            warnings: [],
            actualDataFrom: bars[0].Timestamp,
            simulator,
            weightStrategy: null,
            new CumulativeRsi2Config(),
            CancellationToken.None);

        result.TotalTrades.Should().Be(1);
        result.TotalReturn.Should().Be(1_000m);
        result.TotalReturnPercent.Should().Be(0.01m);
        result.Trades.Should().ContainSingle(trade =>
            trade.EntryPrice == 100m
            && trade.ExitPrice == 110m
            && trade.Quantity == 100
            && trade.ExitReason == "목표 도달");
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

    private sealed class SingleEntryDetector(DateTime entryAt) : IPatternDetector
    {
        public PatternType PatternType => PatternType.Breakout;

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
                    TargetPrice = 110m,
                    AllocationScale = 1m
                }
                : null;
            return Task.FromResult(signal);
        }
    }
}
