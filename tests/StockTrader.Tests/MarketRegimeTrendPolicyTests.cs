using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Patterns;

namespace StockTrader.Tests;

public sealed class MarketRegimeTrendPolicyTests
{
    [Fact]
    public void InsufficientEvidenceFailsClosedWithoutInventingBullishTrend()
    {
        var bars = Bars(StrategyEvaluationPolicy.RegimeTrendBars - 1, 100m);

        var result = MarketRegimeTrendPolicy.Evaluate(bars, bars[^1].Timestamp);

        result.SpyAbove200Ma.Should().BeFalse();
        result.SpyPrice.Should().Be(100m);
        result.Spy200Ma.Should().Be(0m);
        result.RegimeLabel.Should().Be(MarketRegimeTrendPolicy.UnknownLabel);
        result.AsOf.Should().Be(bars[^1].Timestamp);
    }

    [Fact]
    public void EvaluationUsesOnlyTheTrailingCompletedWindowAndIgnoresFutureBars()
    {
        var bars = Bars(StrategyEvaluationPolicy.RegimeTrendBars, 100m).ToList();
        bars[^1].Close = 101m;
        var asOf = bars[^1].Timestamp;
        bars.Insert(0, Bar(asOf.AddDays(1), 10_000m));

        var result = MarketRegimeTrendPolicy.Evaluate(bars, asOf);

        result.SpyAbove200Ma.Should().BeTrue();
        result.SpyPrice.Should().Be(101m);
        result.Spy200Ma.Should().Be(100.005m);
        result.AsOf.Should().Be(asOf);
    }

    [Fact]
    public void PriceEqualToTrendIsBearishAcrossEveryExecutionAdapter()
    {
        var bars = Bars(StrategyEvaluationPolicy.RegimeTrendBars, 100m);
        var asOf = bars[^1].Timestamp;

        var policy = MarketRegimeTrendPolicy.Evaluate(bars, asOf);
        var live = new LiveMarketRegimeEvaluator().Evaluate(bars, asOf);

        policy.SpyAbove200Ma.Should().BeFalse();
        policy.RegimeLabel.Should().Be(MarketRegimeTrendPolicy.BearishLabel);
        live.Should().BeEquivalentTo(policy);
    }

    [Fact]
    public void MissingBacktestRegimeFailsClosedInsteadOfAssumingBullish()
    {
        var result = BacktestExecutionAdapter.GetRegimeForDate(
            new DateOnly(2026, 8, 19), []);

        result.SpyAbove200Ma.Should().BeFalse();
        result.RegimeLabel.Should().Be(MarketRegimeTrendPolicy.UnknownLabel);
    }

    [Fact]
    public async Task BacktestMapUsesTheSameUnknownSemanticsForShortHistory()
    {
        var bars = Bars(StrategyEvaluationPolicy.RegimeTrendBars - 1, 100m);
        var builder = new BacktestRegimeMapBuilder(
            NullLogger<BacktestRegimeMapBuilder>.Instance);

        var result = await builder.BuildAsync(
            new FixedDataFeed(bars),
            bars[^10].Timestamp,
            bars[^1].Timestamp,
            ct: CancellationToken.None);

        result.Should().NotBeNull();
        var last = result![DateOnly.FromDateTime(bars[^1].Timestamp)];
        last.SpyAbove200Ma.Should().BeFalse();
        last.RegimeLabel.Should().Be(MarketRegimeTrendPolicy.UnknownLabel);
        last.SpyPrice.Should().Be(100m);
    }

    private static OhlcvBar[] Bars(int count, decimal close) =>
        Enumerable.Range(0, count)
            .Select(index => Bar(DateTime.UnixEpoch.AddDays(index), close))
            .ToArray();

    private static OhlcvBar Bar(DateTime timestamp, decimal close) => new()
    {
        Symbol = "SPY",
        TimeFrame = TimeFrame.Daily,
        Timestamp = timestamp,
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = 1_000
    };

    private sealed class FixedDataFeed(IReadOnlyList<OhlcvBar> bars) : IDataFeedService
    {
        public Task<List<OhlcvBar>> GetHistoricalBarsAsync(
            string symbol,
            TimeFrame timeFrame,
            DateTime from,
            DateTime to,
            CancellationToken ct = default) =>
            Task.FromResult(bars.ToList());

        public Task<OhlcvBar?> GetLatestBarAsync(
            string symbol,
            TimeFrame timeFrame,
            CancellationToken ct = default) =>
            Task.FromResult<OhlcvBar?>(bars.LastOrDefault());

        public Task<List<OhlcvBar>> GetIntradayBarsAsync(
            string symbol,
            DateTime date,
            CancellationToken ct = default) =>
            Task.FromResult(new List<OhlcvBar>());

        public Task<decimal> GetCurrentPriceAsync(
            string symbol,
            CancellationToken ct = default) =>
            Task.FromResult(bars.LastOrDefault()?.Close ?? 0m);
    }
}
