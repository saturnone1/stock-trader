using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Application.Backtesting;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Backtest;
using StockTrader.Services.DataFeed;
using StockTrader.Services.Indicators;

namespace StockTrader.Tests;

public class BacktestDataPreparerTests
{
    [Fact]
    public async Task PrepareAsync_UsesProvidedIndicatorPolicyForEveryRequestedSymbol()
    {
        var bars = BuildBars(80);
        var feed = new RecordingDataFeed(bars);
        var indicators = new IndicatorService();
        var preparer = new BacktestDataPreparer(
            indicators, NullLogger<BacktestDataPreparer>.Instance);
        var policy = new CumulativeRsi2Config
        {
            RsiPeriod = 5,
            CumulativePeriod = 3,
            LongTrendMaPeriod = 7,
        };
        var tqqqPolicy = new Tqqq200SmaConfig { SmaPeriod = 10, SmaStopMultiplier = 0.98m };

        var result = await preparer.PrepareAsync(
            feed, [" tqqq ", "SPY", "TQQQ"], TimeFrame.Daily,
            bars[60].Timestamp, bars[^1].Timestamp, policy, tqqqPolicy);

        result.HasData.Should().BeTrue();
        result.Symbols.Keys.Should().BeEquivalentTo(["TQQQ", "SPY"]);
        feed.RequestedSymbols.Should().Equal("TQQQ", "SPY");
        var tqqq = result.Symbols["TQQQ"];
        tqqq.CumulativeRsi2.Should().Equal(
            indicators.CumulativeRsi(tqqq.Closes, policy.RsiPeriod, policy.CumulativePeriod));
        tqqq.CumulativeRsi2TrendMa.Should().Equal(
            indicators.SMA(tqqq.Closes, policy.LongTrendMaPeriod));
        tqqq.TqqqProtectiveStopFloor.Should().Equal(
            indicators.SMA(tqqq.Closes, tqqqPolicy.SmaPeriod)
                .Select(value => value > 0 ? value * tqqqPolicy.SmaStopMultiplier : 0m));
    }

    [Fact]
    public async Task Slice_RecomputesDerivedIndicatorsWithoutMutatingFullData()
    {
        var bars = BuildBars(500);
        var feed = new RecordingDataFeed(bars);
        var indicators = new IndicatorService();
        var preparer = new BacktestDataPreparer(
            indicators, NullLogger<BacktestDataPreparer>.Instance);
        var originalPolicy = new CumulativeRsi2Config
        {
            RsiPeriod = 2,
            CumulativePeriod = 2,
            LongTrendMaPeriod = 20,
        };
        var full = await preparer.PrepareAsync(
            feed, ["TQQQ"], TimeFrame.Daily, bars[450].Timestamp, bars[^1].Timestamp,
            originalPolicy, new Tqqq200SmaConfig());
        var originalCumulative = full.Symbols["TQQQ"].CumulativeRsi2.ToArray();
        var slicePolicy = new CumulativeRsi2Config
        {
            RsiPeriod = 6,
            CumulativePeriod = 4,
            LongTrendMaPeriod = 10,
        };

        var sliced = preparer.Slice(
            full.Symbols, ["TQQQ"], TimeFrame.Daily,
            bars[450].Timestamp, bars[^1].Timestamp, slicePolicy,
            new Tqqq200SmaConfig { SmaPeriod = 25, SmaStopMultiplier = 0.97m },
            full.Evidence);

        sliced.HasData.Should().BeTrue();
        // 잘라낸 구간도 원본과 같은 데이터 조건에서 나왔음을 진술해야 한다.
        sliced.Evidence.Should().Be(full.Evidence);
        sliced.Symbols["TQQQ"].CumulativeRsi2.Should().Equal(
            indicators.CumulativeRsi(
                sliced.Symbols["TQQQ"].Closes,
                slicePolicy.RsiPeriod,
                slicePolicy.CumulativePeriod));
        full.Symbols["TQQQ"].CumulativeRsi2.Should().Equal(originalCumulative);
        var sliceStart = Array.FindIndex(
            full.Symbols["TQQQ"].Bars,
            bar => bar.Timestamp == sliced.Symbols["TQQQ"].Bars[0].Timestamp);
        sliced.Symbols["TQQQ"].Atr.Should().Equal(
            full.Symbols["TQQQ"].Atr[sliceStart..],
            "워크포워드 슬라이스는 전체 이력에서 계산된 ATR 값을 보존해야 합니다");
    }

    [Fact]
    public async Task PrepareAsync_ExtendsDailyWarmupForConfiguredTqqqTrendPeriod()
    {
        var bars = BuildBars(80);
        var feed = new RecordingDataFeed(bars);
        var from = bars[60].Timestamp;
        var preparer = new BacktestDataPreparer(
            new IndicatorService(), NullLogger<BacktestDataPreparer>.Instance);

        await preparer.PrepareAsync(
            feed, ["TQQQ"], TimeFrame.Daily, from, bars[^1].Timestamp,
            new CumulativeRsi2Config(),
            new Tqqq200SmaConfig { SmaPeriod = 300 });

        feed.RequestedFrom.Should().ContainSingle()
            .Which.Should().Be(from.AddDays(-450));
    }

    private static List<OhlcvBar> BuildBars(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new OhlcvBar
            {
                Symbol = "TQQQ",
                Timestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
                TimeFrame = TimeFrame.Daily,
                Open = 100 + index,
                High = 102 + index,
                Low = 99 + index,
                Close = 101 + index + index % 3,
                Volume = 1_000_000 + index,
            })
            .ToList();

    private sealed class RecordingDataFeed(List<OhlcvBar> bars) : IDataFeedService
    {
        public DataSource Source => DataSource.Alpaca;
        public List<string> RequestedSymbols { get; } = [];
        public List<DateTime> RequestedFrom { get; } = [];

        public Task<List<OhlcvBar>> GetHistoricalBarsAsync(
            string symbol, TimeFrame timeFrame, DateTime from, DateTime to, CancellationToken ct = default)
        {
            RequestedSymbols.Add(symbol);
            RequestedFrom.Add(from);
            return Task.FromResult(bars.Select(bar => new OhlcvBar
            {
                Symbol = symbol,
                Timestamp = bar.Timestamp,
                TimeFrame = timeFrame,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
            }).ToList());
        }

        public Task<OhlcvBar?> GetLatestBarAsync(string symbol, TimeFrame timeFrame, CancellationToken ct = default) =>
            Task.FromResult<OhlcvBar?>(bars[^1]);

        public Task<List<OhlcvBar>> GetIntradayBarsAsync(string symbol, DateTime date, CancellationToken ct = default) =>
            Task.FromResult(new List<OhlcvBar>());

        public Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default) =>
            Task.FromResult(bars[^1].Close);
    }
}
