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
}
