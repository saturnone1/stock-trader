using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Application.StrategyPreview;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.Backtesting;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.Indicators;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class CustomStrategyExecutionParityTests
{
    [Fact]
    public async Task PreviewAndBacktest_RunTheSameCompiledFractionalScaleOut()
    {
        var bars = Bars();
        bars[51].Open = 100m;
        bars[51].High = 111m;
        bars[51].Low = 99m;
        bars[51].Close = 110m;
        var definition = new StrategyDocument
        {
            Name = "compiled-scaling-parity",
            EntryMode = StrategyCatalog.CurrentCloseEntryMode,
            EntryRulesJson = JsonSerializer.Serialize(new[] { PassingPriceChangeRule() }),
            ScalingRulesJson = JsonSerializer.Serialize(new[]
            {
                new ScalingRule
                {
                    Direction = StrategyCatalog.ScalingOutDirection,
                    Percent = 1.5m,
                    MaxCount = 1,
                    Conditions = [PassingPriceChangeRule()]
                }
            }),
            AtrStopMultiplier = 10m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0
        };
        var compilation = StrategyCompiler.Compile(definition);
        compilation.Errors.Should().BeEmpty();
        var strategy = compilation.Strategy!;
        var indicators = new IndicatorService();
        var atr = Enumerable.Repeat(1m, bars.Length).ToArray();
        var factory = new CustomStrategyDetectorFactory(
            indicators,
            new FixedTimeProvider(
                new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        var preview = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[50].Timestamp,
                bars[^1].Timestamp.AddDays(1),
                bars,
                atr,
                new Dictionary<string, OhlcvBar[]>(),
                [],
                factory.Create(strategy)));

        var backtest = await new BacktestSimulationEngine(
                new BacktestSignalEntryProcessor(
                    NullLogger<BacktestSignalEntryProcessor>.Instance))
            .RunAsync(
                ["AAA"],
                new Dictionary<string, PreparedSymbolData>
                {
                    ["AAA"] = Prepared(bars, atr)
                },
                [factory.Create(strategy)],
                [],
                bars[50].Timestamp,
                bars[^1].Timestamp,
                10_000m,
                0m,
                0m,
                TimeFrame.Daily,
                new BacktestRiskParameters(1m, 0m, 1, 1),
                null,
                SlippageModel.Fixed,
                [],
                bars[0].Timestamp,
                new BacktestExecutionAdapter(),
                null,
                new CumulativeRsi2Config(),
                CancellationToken.None);

        preview.Should().NotBeNull();
        var previewScaleOut = preview!.Markers.Should().ContainSingle(marker =>
            marker.Type == StrategyCatalog.ScalingOutDirection).Subject;
        var backtestScaleOut = backtest.Trades.Should().ContainSingle(trade =>
            trade.ExitReason == "분할 매도(1.5%)").Subject;
        previewScaleOut.Date.Should().Be(backtestScaleOut.ExitTime);
        previewScaleOut.Price.Should().Be(backtestScaleOut.ExitPrice);
        previewScaleOut.Details.Should().Contain("(1주)");
        backtestScaleOut.Quantity.Should().Be(1);
        (preview.Summary.TotalReturnPercent / 100m)
            .Should().BeApproximately(backtest.TotalReturnPercent, 0.0000001m);
    }

    [Fact]
    public void ScalingStrategy_IsRejectedForLiveUntilBrokerExecutionHasParity()
    {
        var definition = new StrategyDocument
        {
            Name = "scaling-live-boundary",
            TimeFrame = TimeFrame.Daily,
            EntryMode = StrategyCatalog.NextOpenEntryMode,
            EnableLiveTrading = true,
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_CHANGE",
                    Operator = ">=",
                    Value = -100m,
                    Params = new Dictionary<string, decimal> { ["bars"] = 1m }
                }
            }),
            ScalingRulesJson = JsonSerializer.Serialize(new[]
            {
                new ScalingRule
                {
                    Direction = StrategyCatalog.ScalingInDirection,
                    Percent = 15m,
                    MaxCount = 1,
                    Conditions =
                    [
                        new EntryRule
                        {
                            Indicator = "PRICE_CHANGE",
                            Operator = ">=",
                            Value = -100m,
                            Params = new Dictionary<string, decimal> { ["bars"] = 1m }
                        }
                    ]
                }
            })
        };

        var compilation = StrategyCompiler.Compile(definition);

        compilation.Strategy.Should().BeNull();
        compilation.Errors.Should().ContainSingle(error =>
            error.Contains("추가 매수·분할 매도", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MultiSymbolBacktest_MatchesPerSymbolPreviewWithoutLeakingIndicatorCache()
    {
        var rising = Bars("RISING", 100m);
        rising[50].Open = 110m;
        rising[50].High = 111m;
        rising[50].Low = 109m;
        rising[50].Close = 110m;
        rising[51].Open = 110m;
        rising[51].High = 120m;
        rising[51].Low = 109m;
        rising[51].Close = 115m;

        var falling = Bars("FALLING", 200m);
        falling[50].Open = 190m;
        falling[50].High = 191m;
        falling[50].Low = 189m;
        falling[50].Close = 190m;
        falling[51].Open = 190m;
        falling[51].High = 191m;
        falling[51].Low = 180m;
        falling[51].Close = 185m;

        var definition = new StrategyDocument
        {
            Name = "multi-symbol-cache-parity",
            EntryMode = StrategyCatalog.CurrentCloseEntryMode,
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_VS_SMA",
                    Operator = "crosses_above",
                    Value = 0m,
                    Params = new Dictionary<string, decimal> { ["period"] = 20m }
                }
            }),
            AtrStopMultiplier = 2m,
            AtrTargetMultiplier = 3m,
            MaxHoldingBars = 10
        };
        var compilation = StrategyCompiler.Compile(definition);
        compilation.Errors.Should().BeEmpty();
        var strategy = compilation.Strategy!;
        var indicators = new IndicatorService();
        var risingAtr = Enumerable.Repeat(1m, rising.Length).ToArray();
        var fallingAtr = Enumerable.Repeat(1m, falling.Length).ToArray();
        var factory = new CustomStrategyDetectorFactory(
            indicators,
            new FixedTimeProvider(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        async Task<PatternPreviewResult> Preview(string symbol, OhlcvBar[] bars, decimal[] atr) =>
            (await new PatternPreviewSimulationEngine().RunAsync(
                new PatternPreviewSimulationInput(
                    symbol,
                    TimeFrame.Daily,
                    bars[50].Timestamp,
                    bars[^1].Timestamp.AddDays(1),
                    bars,
                    atr,
                    new Dictionary<string, OhlcvBar[]>(),
                    [],
                    factory.Create(strategy))))!;

        var risingPreview = await Preview("RISING", rising, risingAtr);
        var fallingPreview = await Preview("FALLING", falling, fallingAtr);
        var backtest = await new BacktestSimulationEngine(
                new BacktestSignalEntryProcessor(NullLogger<BacktestSignalEntryProcessor>.Instance))
            .RunAsync(
                ["RISING", "FALLING"],
                new Dictionary<string, PreparedSymbolData>
                {
                    ["RISING"] = Prepared(rising, risingAtr),
                    ["FALLING"] = Prepared(falling, fallingAtr)
                },
                [factory.Create(strategy)],
                [],
                rising[50].Timestamp,
                rising[^1].Timestamp,
                100_000m,
                0m,
                0m,
                TimeFrame.Daily,
                new BacktestRiskParameters(0.01m, 0m, 2, 2),
                null,
                SlippageModel.Fixed,
                [],
                rising[0].Timestamp,
                new BacktestExecutionAdapter(),
                null,
                new CumulativeRsi2Config(),
                CancellationToken.None);

        fallingPreview.Markers.Should().NotContain(marker => marker.Type == "ENTRY");
        var previewEntry = risingPreview.Markers.Single(marker => marker.Type == "ENTRY");
        var previewExit = risingPreview.Markers.Single(marker => marker.Type == "EXIT");
        var trade = backtest.Trades.Should().ContainSingle().Subject;
        trade.Symbol.Should().Be("RISING");
        trade.EntryTime.Should().Be(previewEntry.Date);
        trade.EntryPrice.Should().Be(previewEntry.Price);
        trade.ExitTime.Should().Be(previewExit.Date);
        trade.ExitPrice.Should().Be(previewExit.Price);
        trade.ExitReason.Should().Be(previewExit.Reason);
    }

    [Fact]
    public async Task PreviewBacktestAndLiveFill_RunTheSameCompiledNextOpenStrategy()
    {
        var bars = Bars();
        bars[51].Open = 105m;
        bars[51].High = 120m;
        bars[51].Low = 104m;
        bars[51].Close = 115m;
        var definition = new StrategyDocument
        {
            Name = "compiled-strategy-parity",
            EntryMode = StrategyCatalog.NextOpenEntryMode,
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_CHANGE",
                    Operator = ">=",
                    Value = -100m,
                    Params = new Dictionary<string, decimal> { ["bars"] = 1m }
                }
            }),
            AtrStopMultiplier = 2m,
            AtrTargetMultiplier = 3m,
            MaxHoldingBars = 10
        };
        var compilation = StrategyCompiler.Compile(definition);
        compilation.Errors.Should().BeEmpty();
        var strategy = compilation.Strategy!;
        var indicators = new IndicatorService();
        var atr = indicators.ATR(bars, 14);
        var factory = new CustomStrategyDetectorFactory(
            indicators,
            new FixedTimeProvider(
                new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero)));

        var preview = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[50].Timestamp,
                bars[^1].Timestamp.AddDays(1),
                bars,
                atr,
                new Dictionary<string, OhlcvBar[]>(),
                [],
                factory.Create(strategy)));

        var backtest = await new BacktestSimulationEngine(
                new BacktestSignalEntryProcessor(
                    NullLogger<BacktestSignalEntryProcessor>.Instance))
            .RunAsync(
                ["AAA"],
                new Dictionary<string, PreparedSymbolData>
                {
                    ["AAA"] = Prepared(bars, atr)
                },
                [factory.Create(strategy)],
                [],
                bars[50].Timestamp,
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

        var liveRuntime = factory.Create(strategy);
        var liveSignal = await liveRuntime.EvaluateEntryAsync(
            "AAA",
            bars[..51],
            new MarketRegime { SpyAbove200Ma = true },
            CancellationToken.None);
        liveSignal.Should().NotBeNull();
        var livePosition = LiveEntryPositionFactory.Create(
            new TradeRecommendation
            {
                Symbol = "AAA",
                PatternType = PatternType.Custom,
                CustomPatternName = strategy.Name,
                EntryPrice = liveSignal!.EntryPrice,
                StopLossPrice = liveSignal.StopLossPrice,
                TargetPrice = liveSignal.TargetPrice,
                ShareQuantity = 1,
            },
            new Position
            {
                Symbol = "AAA",
                Quantity = 1,
                EntryPrice = bars[51].Open,
                CurrentPrice = bars[51].Open,
            },
            accountId: 1,
            preview!.Markers.Single(marker => marker.Type == "ENTRY").Date);

        var previewEntry = preview.Markers.Single(marker => marker.Type == "ENTRY");
        var previewExit = preview.Markers.Single(marker => marker.Type == "EXIT");
        var backtestTrade = backtest.Trades.Should().ContainSingle().Subject;
        var repository = new Mock<IOhlcvRepository>();
        repository.Setup(value => value.GetBarsAsync(
                "AAA", TimeFrame.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars.ToList());
        var patternSettings = new Mock<IOptionsMonitor<PatternSettings>>();
        patternSettings.SetupGet(value => value.CurrentValue).Returns(new PatternSettings());
        livePosition.CurrentPrice = previewExit.Price;
        livePosition.HighSinceEntry = previewExit.Price;
        livePosition.EntryAtr = atr[50];
        var liveExit = await new LivePositionExitEvaluator(
                indicators,
                factory,
                patternSettings.Object,
                new FixedTimeProvider(
                    new DateTimeOffset(bars[^1].Timestamp, TimeSpan.Zero)),
                NullLogger<LivePositionExitEvaluator>.Instance)
            .EvaluateAsync(
                livePosition,
                strategy,
                repository.Object,
                null,
                CancellationToken.None);

        previewEntry.Date.Should().Be(backtestTrade.EntryTime);
        previewEntry.Price.Should().Be(backtestTrade.EntryPrice);
        previewEntry.Price.Should().Be(livePosition.EntryPrice);
        previewEntry.StopPrice.Should().Be(livePosition.StopLossPrice);
        previewEntry.TargetPrice.Should().Be(livePosition.TargetPrice);
        previewExit.Date.Should().Be(backtestTrade.ExitTime);
        previewExit.Price.Should().Be(backtestTrade.ExitPrice);
        previewExit.Reason.Should().Be(backtestTrade.ExitReason);
        liveExit.ShouldExit.Should().BeTrue();
        liveExit.Reason.Should().Be(previewExit.Reason);
    }

    private static PreparedSymbolData Prepared(OhlcvBar[] bars, decimal[] atr)
    {
        var closes = bars.Select(bar => bar.Close).ToArray();
        return new PreparedSymbolData(
            bars,
            atr,
            closes,
            new decimal[bars.Length],
            new decimal[bars.Length],
            new decimal[bars.Length],
            bars.Select((bar, index) => (bar.Timestamp, index))
                .ToDictionary(pair => pair.Timestamp, pair => pair.index));
    }

    private static EntryRule PassingPriceChangeRule() => new()
    {
        Indicator = "PRICE_CHANGE",
        Operator = ">=",
        Value = -100m,
        Params = new Dictionary<string, decimal> { ["bars"] = 1m }
    };

    private static OhlcvBar[] Bars(string symbol = "AAA", decimal basePrice = 100m) =>
        Enumerable.Range(0, 52)
        .Select(index => new OhlcvBar
        {
            Symbol = symbol,
            TimeFrame = TimeFrame.Daily,
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            Open = basePrice,
            High = basePrice + 1m,
            Low = basePrice - 1m,
            Close = basePrice,
            Volume = 1_000_000
        })
        .ToArray();

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
