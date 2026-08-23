using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;
using StockTrader.Services.Order;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public class LivePositionExecutionEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_UsesCompiledPartialProfitPolicyAndDefersStateUntilFill()
    {
        var compilation = StrategyCompiler.Compile(new StrategyDocument
        {
            Name = "live-partial-parity",
            TimeFrame = TimeFrame.Daily,
            EntryMode = StrategyCatalog.NextOpenEntryMode,
            AtrStopMultiplier = 5m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0,
            PartialProfitR = 1m,
            EntryRulesJson = JsonSerializer.Serialize(new[]
            {
                new EntryRule
                {
                    Indicator = "PRICE_CHANGE",
                    Operator = ">=",
                    Value = -100m,
                    Params = new Dictionary<string, decimal> { ["bars"] = 1m },
                },
            }),
        });
        compilation.Errors.Should().BeEmpty();

        var now = new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 60).Select(index => new OhlcvBar
        {
            Symbol = "AAA",
            TimeFrame = TimeFrame.Daily,
            Timestamp = now.AddDays(index - 59),
            Open = 100m,
            High = 100m,
            Low = 100m,
            Close = 100m,
            Volume = 1_000,
        }).ToList();
        var repository = new Mock<IOhlcvRepository>();
        repository.Setup(value => value.GetBarsAsync(
                "AAA", TimeFrame.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);
        var settings = new Mock<IOptionsMonitor<PatternSettings>>();
        settings.SetupGet(value => value.CurrentValue).Returns(new PatternSettings());
        var position = new Position
        {
            Symbol = "AAA",
            PatternType = PatternType.Custom,
            CustomPatternName = compilation.Strategy!.Name,
            Quantity = 10,
            InitialQuantity = 10,
            EntryPrice = 100m,
            CurrentPrice = 105m,
            StopLossPrice = 95m,
            TargetPrice = 600m,
            InitialRiskDistance = 5m,
            EntryAtr = 1m,
            HighSinceEntry = 100m,
            OpenedAt = now.AddDays(-2),
        };
        var indicators = new IndicatorService();
        var evaluator = new LivePositionExecutionEvaluator(
            indicators,
            new CustomStrategyDetectorFactory(),
            settings.Object,
            new FixedTimeProvider(new DateTimeOffset(now, TimeSpan.Zero)),
            NullLogger<LivePositionExecutionEvaluator>.Instance);

        var result = await evaluator.EvaluateAsync(
            position, compilation.Strategy, repository.Object, null);

        result.ShouldExecute.Should().BeTrue();
        result.Intent!.Quantity.Should().Be(5);
        result.Intent.MarksPartialProfit.Should().BeTrue();
        result.Reason.Should().Be("부분 익절(1R)");
        position.Quantity.Should().Be(10);
        position.PartialProfitTaken.Should().BeFalse();
        position.StopLossPrice.Should().Be(95m);
        position.BreakevenApplied.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_EmitsScaleInWithinCapitalCapAndHonorsPersistedCount()
    {
        var strategy = CompileScalingStrategy(
            StrategyCatalog.ScalingInDirection, percent: 50m, maxCount: 1);
        var (evaluator, repository, position) = Scenario(strategy);

        var result = await evaluator.EvaluateAsync(
            position,
            strategy,
            repository,
            null,
            currentEquity: 20_000m,
            maxTotalPositions: 10);

        result.Intent.Should().Be(new LiveLongPositionExecutionIntent(
            5,
            "추가 매수(50%)",
            PositionExecutionKind.ScaleIn,
            ScalingRuleIndex: 0));
        position.Quantity.Should().Be(10);
        position.EntryPrice.Should().Be(100m);

        position.ScalingExecutions.Add(new PositionScalingExecution
        {
            PositionId = position.Id,
            Position = position,
            RuleIndex = 0,
            ExecutionCount = 1,
        });
        var afterMaximum = await evaluator.EvaluateAsync(
            position,
            strategy,
            repository,
            null,
            currentEquity: 20_000m,
            maxTotalPositions: 10);

        afterMaximum.Intent.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_FailsScaleInClosedWithoutEquityButAllowsScaleOut()
    {
        var scaleInStrategy = CompileScalingStrategy(
            StrategyCatalog.ScalingInDirection, percent: 50m, maxCount: 1);
        var (scaleInEvaluator, scaleInRepository, scaleInPosition) = Scenario(scaleInStrategy);

        var scaleIn = await scaleInEvaluator.EvaluateAsync(
            scaleInPosition, scaleInStrategy, scaleInRepository, null);

        scaleIn.Intent.Should().BeNull();

        var scaleOutStrategy = CompileScalingStrategy(
            StrategyCatalog.ScalingOutDirection, percent: 40m, maxCount: 1);
        var (scaleOutEvaluator, scaleOutRepository, scaleOutPosition) = Scenario(scaleOutStrategy);
        var scaleOut = await scaleOutEvaluator.EvaluateAsync(
            scaleOutPosition, scaleOutStrategy, scaleOutRepository, null);

        scaleOut.Intent.Should().Be(new LiveLongPositionExecutionIntent(
            4,
            "분할 매도(40%)",
            PositionExecutionKind.ScaleOut,
            ScalingRuleIndex: 0));
    }

    [Fact]
    public async Task EvaluateAsync_CustomExitUsesCanonicalSharedReason()
    {
        var passingRule = new EntryRule
        {
            Indicator = "PRICE_CHANGE",
            Operator = ">=",
            Value = -100m,
            Params = new Dictionary<string, decimal> { ["bars"] = 1m },
        };
        var compilation = StrategyCompiler.Compile(new StrategyDocument
        {
            Name = "live-canonical-exit",
            TimeFrame = TimeFrame.Daily,
            EntryRulesJson = JsonSerializer.Serialize(new[] { passingRule }),
            ExitRulesJson = JsonSerializer.Serialize(new[] { passingRule }),
            AtrStopMultiplier = 10m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0,
        });
        compilation.Errors.Should().BeEmpty();
        var strategy = compilation.Strategy!;
        var (evaluator, repository, position) = Scenario(strategy);

        var result = await evaluator.EvaluateAsync(
            position,
            strategy,
            repository,
            null);

        result.Intent.Should().Be(new LiveLongPositionExecutionIntent(
            position.Quantity,
            LongPositionExecutionReasons.StrategyRuleExit,
            PositionExecutionKind.FullExit));
    }

    private static CompiledStrategy CompileScalingStrategy(
        string direction,
        decimal percent,
        int maxCount)
    {
        var passingRule = new EntryRule
        {
            Indicator = "PRICE_CHANGE",
            Operator = ">=",
            Value = -100m,
            Params = new Dictionary<string, decimal> { ["bars"] = 1m },
        };
        var compilation = StrategyCompiler.Compile(new StrategyDocument
        {
            Name = $"live-{direction.ToLowerInvariant()}",
            TimeFrame = TimeFrame.Daily,
            EntryMode = StrategyCatalog.NextOpenEntryMode,
            EntryRulesJson = JsonSerializer.Serialize(new[] { passingRule }),
            ScalingRulesJson = JsonSerializer.Serialize(new[]
            {
                new ScalingRule
                {
                    Direction = direction,
                    Percent = percent,
                    MaxCount = maxCount,
                    Conditions = [passingRule],
                },
            }),
            PortfolioRulesJson = JsonSerializer.Serialize(new PortfolioRulesConfig
            {
                MaxSinglePositionPercent = 20m,
            }),
            AtrStopMultiplier = 10m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0,
        });
        compilation.Errors.Should().BeEmpty();
        return compilation.Strategy!;
    }

    private static (LivePositionExecutionEvaluator Evaluator, IOhlcvRepository Repository, Position Position)
        Scenario(CompiledStrategy strategy)
    {
        var now = new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);
        var bars = Enumerable.Range(0, 60).Select(index => new OhlcvBar
        {
            Symbol = "AAA",
            TimeFrame = TimeFrame.Daily,
            Timestamp = now.AddDays(index - 59),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            Volume = 1_000,
        }).ToList();
        var repository = new Mock<IOhlcvRepository>();
        repository.Setup(value => value.GetBarsAsync(
                "AAA", TimeFrame.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);
        var settings = new Mock<IOptionsMonitor<PatternSettings>>();
        settings.SetupGet(value => value.CurrentValue).Returns(new PatternSettings());
        var indicators = new IndicatorService();
        var clock = new FixedTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        var evaluator = new LivePositionExecutionEvaluator(
            indicators,
            new CustomStrategyDetectorFactory(),
            settings.Object,
            clock,
            NullLogger<LivePositionExecutionEvaluator>.Instance);
        var position = new Position
        {
            Id = 7,
            Symbol = "AAA",
            PatternType = PatternType.Custom,
            CustomPatternName = strategy.Name,
            Quantity = 10,
            InitialQuantity = 10,
            EntryPrice = 100m,
            CurrentPrice = 100m,
            StopLossPrice = 50m,
            TargetPrice = 1_000m,
            InitialRiskDistance = 50m,
            EntryAtr = 1m,
            HighSinceEntry = 100m,
            OpenedAt = now.AddDays(-2),
        };
        return (evaluator, repository.Object, position);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
