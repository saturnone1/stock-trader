using System.Text.Json;
using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Application.StrategyPreview;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Indicators;

namespace StockTrader.Tests;

public class PatternPreviewSimulationGoldenTests
{
    [Fact]
    public async Task RunAsync_NextOpenRepricesAndEvaluatesEntryBarLikeBacktest()
    {
        var bars = Bars();
        bars[51].Open = 105m;
        bars[51].High = 116m;
        bars[51].Low = 104m;
        bars[51].Close = 115m;
        var strategy = Compile(new StrategyDocument
        {
            Name = "preview-next-open-golden",
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
        });
        var runtime = new SingleEntryRuntime(strategy, bars[50].Timestamp);

        var result = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[50].Timestamp,
                bars[^1].Timestamp.AddDays(1),
                bars,
                Enumerable.Repeat(5m, bars.Length).ToArray(),
                new Dictionary<string, OhlcvBar[]>(),
                [],
                runtime));

        result.Should().NotBeNull();
        result!.Markers.Should().ContainInOrder(
            new PatternPreviewMarker(
                bars[51].Timestamp, "ENTRY", 105m, 100m, 115m,
                "golden signal · 매수 비중 100%"),
            new PatternPreviewMarker(
                bars[51].Timestamp, "EXIT", 115m, Reason: "목표 도달"));
        result.Summary.CompletedTrades.Should().Be(1);
        result.Summary.WinningTrades.Should().Be(1);
        result.Summary.TotalReturnPercent.Should().BeApproximately(
            1000m / 10500m * 100m, 0.0000001m);
        result.Summary.OpenPosition.Should().BeFalse();
        result.Warnings.Should().Contain(MarketRegimeTrendPolicy.InsufficientHistoryWarning);
        var lastTimestamp = bars[^1].Timestamp;
        runtime.ReferenceAsOf.Should().OnlyContain(
            timestamp => timestamp <= lastTimestamp);
    }

    [Fact]
    public async Task RunAsync_RejectsMismatchedPreparedIndicatorLength()
    {
        var bars = Bars();
        var strategy = Compile(new StrategyDocument
        {
            Name = "indicator-boundary",
            EntryRulesJson = """[{"indicator":"PRICE_CHANGE","operator":">","value":0,"params":{"bars":1}}]"""
        });

        var act = () => new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[50].Timestamp,
                bars[^1].Timestamp.AddDays(1),
                bars,
                new decimal[bars.Length - 1],
                new Dictionary<string, OhlcvBar[]>(),
                [],
                new SingleEntryRuntime(strategy, bars[50].Timestamp)));

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void PreparedAtr_IsPrefixStableAndCannotLeakFutureBars()
    {
        var bars = Bars();
        for (var index = 0; index < bars.Length; index++)
        {
            bars[index].High += index % 7;
            bars[index].Low -= index % 5;
            bars[index].Close += index % 3;
        }
        var indicators = new IndicatorService();
        var prepared = indicators.ATR(bars, 14);

        for (var length = 15; length <= bars.Length; length++)
        {
            var prefix = indicators.ATR(bars[..length], 14);
            prepared[length - 1].Should().Be(prefix[^1],
                "the preview engine may precompute ATR only if each value uses its past prefix");
        }
    }

    [Fact]
    public async Task RunAsync_DoesNotEvaluateTheRepositoryInclusiveEndBar()
    {
        var bars = Bars();
        bars[51].Low = 90m;
        var strategy = Compile(new StrategyDocument
        {
            Name = "exclusive-preview-end",
            EntryRulesJson = """[{"indicator":"PRICE_CHANGE","operator":">","value":0,"params":{"bars":1}}]""",
            AtrStopMultiplier = 1m,
            AtrTargetMultiplier = 2m,
            MaxHoldingBars = 10
        });

        var result = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[50].Timestamp,
                bars[51].Timestamp,
                bars,
                Enumerable.Repeat(5m, bars.Length).ToArray(),
                new Dictionary<string, OhlcvBar[]>(),
                [],
                new SingleEntryRuntime(strategy, bars[50].Timestamp)));

        result.Should().NotBeNull();
        result!.Markers.Should().ContainSingle(marker => marker.Type == "ENTRY");
        result.Markers.Should().NotContain(marker => marker.Type == "EXIT");
        result.Summary.OpenPosition.Should().BeTrue(
            "the bar exactly at DataTo is outside the requested interval even when the repository returns it");
    }

    [Fact]
    public async Task RunAsync_UsesSharedLossCooldownAndCircuitBreakerTransitions()
    {
        var bars = Bars(60);
        foreach (var bar in bars)
            bar.Low = 90m;
        var strategy = Compile(new StrategyDocument
        {
            Name = "shared-loss-transition-golden",
            EntryMode = StrategyCatalog.CurrentCloseEntryMode,
            EntryRulesJson =
                """[{"indicator":"PRICE_CHANGE","operator":">=","value":-100,"params":{"bars":1}}]""",
            ReentryJson = """{"cooldownBarsAfterLoss":2,"cooldownBarsAfterWin":0}""",
            CircuitBreakerJson =
                """{"consecutiveLossLimit":2,"cooldownBars":3,"maxDrawdownPercent":0}""",
            AtrStopMultiplier = 1m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0
        });

        var result = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[49].Timestamp,
                bars[59].Timestamp,
                bars,
                Enumerable.Repeat(5m, bars.Length).ToArray(),
                new Dictionary<string, OhlcvBar[]>(),
                [],
                new AlwaysSignalRuntime(strategy)));

        result.Should().NotBeNull();
        result!.Markers.Where(marker => marker.Type == "ENTRY")
            .Select(marker => marker.Date)
            .Should().Equal(
                bars[49].Timestamp,
                bars[53].Timestamp,
                bars[58].Timestamp);
        result.Markers.Where(marker => marker.Type == "EXIT")
            .Select(marker => marker.Date)
            .Should().Equal(bars[50].Timestamp, bars[54].Timestamp);
        result.Summary.SafetyBlockedEntries.Should().Be(7);
        result.Summary.OpenPosition.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ObservesDrawdownOnPartialRealizationsNotOnlyAtFullClose()
    {
        // 부분청산으로 손실이 실현되면 백테스트는 그 시점에 드로다운을 관측해
        // 서킷브레이커를 발동시킨다. 미리보기가 전량 청산까지 기다리면 같은 전략이
        // 미리보기에서만 차단 없이 계속 진입한다.
        var bars = Bars(70);
        foreach (var bar in bars)
            bar.Low = 90m;

        var strategy = Compile(new StrategyDocument
        {
            Name = "partial-realization-drawdown",
            EntryMode = StrategyCatalog.CurrentCloseEntryMode,
            EntryRulesJson =
                """[{"indicator":"PRICE_CHANGE","operator":">=","value":-100,"params":{"bars":1}}]""",
            CircuitBreakerJson =
                """{"consecutiveLossLimit":0,"cooldownBars":0,"maxDrawdownPercent":1}""",
            AtrStopMultiplier = 1m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0
        });

        var result = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[49].Timestamp,
                bars[69].Timestamp,
                bars,
                Enumerable.Repeat(5m, bars.Length).ToArray(),
                new Dictionary<string, OhlcvBar[]>(),
                [],
                new AlwaysSignalRuntime(strategy)));

        result.Should().NotBeNull();
        // 손실이 실현되어 최대낙폭 한도를 넘으면 이후 진입은 영구 차단된다.
        result!.Summary.SafetyBlockedEntries.Should().BeGreaterThan(0);
        result.Markers.Count(marker => marker.Type == "ENTRY")
            .Should().BeLessThan(bars.Length - 49);
    }

    [Fact]
    public async Task RunAsync_RechecksNextOpenEligibilityAtTheFillBarNotTheSignalBar()
    {
        // 손절이 매 봉 걸리도록 저가를 낮춰 연속 손실을 만든다. 차기봉 진입 전략에서
        // 신호 봉과 체결 봉 사이에 연속손실 차단이 걸리면, 신호 시점에 확정해 버린
        // 진입은 그 차단을 무시한 채 체결된다. 백테스트는 체결 봉에서 자격을 다시
        // 확인하므로 미리보기도 같은 시점에 확인해야 한다.
        var bars = Bars(60);
        foreach (var bar in bars)
            bar.Low = 90m;
        var strategy = Compile(new StrategyDocument
        {
            Name = "next-open-refill-eligibility",
            EntryMode = StrategyCatalog.NextOpenEntryMode,
            EntryRulesJson =
                """[{"indicator":"PRICE_CHANGE","operator":">=","value":-100,"params":{"bars":1}}]""",
            CircuitBreakerJson =
                """{"consecutiveLossLimit":2,"cooldownBars":5,"maxDrawdownPercent":0}""",
            AtrStopMultiplier = 1m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0
        });

        var result = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[49].Timestamp,
                bars[59].Timestamp,
                bars,
                Enumerable.Repeat(5m, bars.Length).ToArray(),
                new Dictionary<string, OhlcvBar[]>(),
                [],
                new AlwaysSignalRuntime(strategy)));

        result.Should().NotBeNull();
        var entries = result!.Markers.Where(marker => marker.Type == "ENTRY").ToArray();

        // 진입은 항상 신호 봉 다음 봉의 시가에 체결된다.
        foreach (var entry in entries)
        {
            var entryBar = bars.Single(bar => bar.Timestamp == entry.Date);
            entry.Price.Should().Be(entryBar.Open);
        }

        // 연속손실 차단이 걸린 구간에서는 신호가 계속 나더라도 체결되지 않는다.
        // 차단 없이 신호마다 체결했다면 평가 구간의 거의 모든 봉에 진입이 생겼을 것이다.
        entries.Length.Should().BeLessThan(5);
        result.Summary.SafetyBlockedEntries.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RunAsync_EvaluatesTheFirstBarThatHasTheRequiredWarmupHistory()
    {
        // 인덱스 49 인 봉의 이력은 bars[0..49], 곧 50 개다. 최소 봉 수를 충족하므로
        // 평가되어야 하며, 백테스트도 같은 인덱스부터 평가한다.
        var bars = Bars(60);
        var strategy = Compile(new StrategyDocument
        {
            Name = "warmup-boundary-golden",
            EntryMode = StrategyCatalog.CurrentCloseEntryMode,
            EntryRulesJson =
                """[{"indicator":"PRICE_CHANGE","operator":">=","value":-100,"params":{"bars":1}}]""",
            AtrStopMultiplier = 1m,
            AtrTargetMultiplier = 100m,
            MaxHoldingBars = 0
        });

        var result = await new PatternPreviewSimulationEngine().RunAsync(
            new PatternPreviewSimulationInput(
                "AAA",
                TimeFrame.Daily,
                bars[0].Timestamp,
                bars[59].Timestamp,
                bars,
                Enumerable.Repeat(5m, bars.Length).ToArray(),
                new Dictionary<string, OhlcvBar[]>(),
                [],
                new AlwaysSignalRuntime(strategy)));

        result.Should().NotBeNull();
        StrategyEvaluationPolicy.FirstEvaluableBarIndex.Should().Be(49);
        result!.Markers.Where(marker => marker.Type == "ENTRY")
            .Select(marker => marker.Date)
            .Should().Contain(
                bars[StrategyEvaluationPolicy.FirstEvaluableBarIndex].Timestamp)
            .And.NotContain(
                bars[StrategyEvaluationPolicy.FirstEvaluableBarIndex - 1].Timestamp);
    }

    private static CompiledStrategy Compile(StrategyDocument definition)
    {
        var result = StrategyCompiler.Compile(definition);
        result.Errors.Should().BeEmpty();
        return result.Strategy!;
    }

    private static OhlcvBar[] Bars(int count = 52) => Enumerable.Range(0, count)
        .Select(index => new OhlcvBar
        {
            Symbol = "AAA",
            TimeFrame = TimeFrame.Daily,
            Timestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            Volume = 1_000_000
        })
        .ToArray();

    private sealed class SingleEntryRuntime(
        CompiledStrategy strategy,
        DateTime entryAt) : ICompiledStrategyRuntime
    {
        public CompiledStrategy Strategy => strategy;
        public bool HasExitRules => false;
        public bool HasScalingRules => false;
        public List<DateTime> ReferenceAsOf { get; } = [];

        public void SetReferenceData(
            Dictionary<string, OhlcvBar[]> referenceData,
            DateTime? asOf = null)
        {
            if (asOf.HasValue) ReferenceAsOf.Add(asOf.Value);
        }

        public Task<PatternSignal?> EvaluateEntryAsync(
            string symbol,
            OhlcvBar[] bars,
            MarketRegime regime,
            CancellationToken ct = default)
        {
            PatternSignal? signal = bars[^1].Timestamp == entryAt
                ? new PatternSignal
                {
                    Symbol = symbol,
                    EntryPrice = 100m,
                    StopLossPrice = 95m,
                    TargetPrice = 110m,
                    AllocationScale = 1m,
                    Details = "golden signal"
                }
                : null;
            return Task.FromResult(signal);
        }

        public bool ShouldExit(OhlcvBar[] bars) => false;

        public ScalingRuleMatch? EvaluateScaling(
            OhlcvBar[] bars,
            decimal currentProfitPercent,
            IReadOnlyDictionary<int, int> scaleCounts) => null;
    }

    private sealed class AlwaysSignalRuntime(CompiledStrategy strategy) : ICompiledStrategyRuntime
    {
        public CompiledStrategy Strategy => strategy;
        public bool HasExitRules => false;
        public bool HasScalingRules => false;

        public void SetReferenceData(
            Dictionary<string, OhlcvBar[]> referenceData,
            DateTime? asOf = null)
        {
        }

        public Task<PatternSignal?> EvaluateEntryAsync(
            string symbol,
            OhlcvBar[] bars,
            MarketRegime regime,
            CancellationToken ct = default) => Task.FromResult<PatternSignal?>(new PatternSignal
        {
            Symbol = symbol,
            EntryPrice = bars[^1].Close,
            StopLossPrice = 95m,
            TargetPrice = 600m,
            AllocationScale = 1m,
            Details = "always"
        });

        public bool ShouldExit(OhlcvBar[] bars) => false;

        public ScalingRuleMatch? EvaluateScaling(
            OhlcvBar[] bars,
            decimal currentProfitPercent,
            IReadOnlyDictionary<int, int> scaleCounts) => null;
    }
}
