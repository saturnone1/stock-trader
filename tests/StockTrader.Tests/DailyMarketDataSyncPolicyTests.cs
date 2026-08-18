using FluentAssertions;
using StockTrader.Application.MarketData;
using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;

namespace StockTrader.Tests;

public class DailyMarketDataSyncPolicyTests
{
    [Fact]
    public void ResolveRequiredSymbols_AddsAndNormalizesProviderBenchmark()
    {
        DailyMarketDataSyncPolicy.ResolveRequiredSymbols(
                [" aapl ", "AAPL", ""],
                DataSource.Yahoo)
            .Should().Equal("AAPL", DataProviderCatalog.UnitedStatesRegimeBenchmark);

        DailyMarketDataSyncPolicy.ResolveRequiredSymbols(
                ["005930"],
                DataSource.LsSecurities)
            .Should().Equal("005930", DataProviderCatalog.KoreaRegimeBenchmark);
    }

    [Fact]
    public void MinimumRequiredBars_DistinguishesBenchmarkFromScannedSymbol()
    {
        DailyMarketDataSyncPolicy.MinimumRequiredBars("spy", DataSource.Alpaca)
            .Should().Be(StrategyEvaluationPolicy.RegimeTrendBars);
        DailyMarketDataSyncPolicy.MinimumRequiredBars("TQQQ", DataSource.Alpaca)
            .Should().Be(StrategyEvaluationPolicy.LiveScannerMinimumBars);
    }

    [Theory]
    [InlineData("2026-08-18T16:59:59", true, false)]
    [InlineData("2026-08-18T17:00:00", true, true)]
    [InlineData("2026-08-22T17:00:00", false, false)]
    public void EvaluateWindowUsesTheProviderMarketsLocalDateAndCloseDelay(
        string localTimestamp,
        bool expectedTradingDay,
        bool expectedReady)
    {
        var local = DateTime.Parse(localTimestamp, System.Globalization.CultureInfo.InvariantCulture);

        var result = DailyMarketDataSyncPolicy.EvaluateWindow(
            local,
            new TimeSpan(16, 0, 0),
            TimeSpan.FromHours(1));

        result.MarketDate.Should().Be(DateOnly.FromDateTime(local));
        result.IsTradingDay.Should().Be(expectedTradingDay);
        result.IsReady.Should().Be(expectedReady);
    }

    [Fact]
    public void CompletedDailyTimestampExcludesTheCurrentSessionUntilItsWindowIsReady()
    {
        var beforeClose = new DailyMarketDataSyncWindow(
            new DateOnly(2026, 8, 18), true, false);
        var afterClose = beforeClose with { IsReady = true };

        DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(
            new DateTime(2026, 8, 17), beforeClose).Should().BeTrue();
        DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(
            new DateTime(2026, 8, 18), beforeClose).Should().BeFalse();
        DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(
            new DateTime(2026, 8, 18), afterClose).Should().BeTrue();
        DailyMarketDataSyncPolicy.IsCompletedDailyTimestamp(
            new DateTime(2026, 8, 19), afterClose).Should().BeFalse();
    }
}
