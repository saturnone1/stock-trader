using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.Tests;

public class BacktestPortfolioStateTests
{
    [Fact]
    public void RecordMarkedEquity_IncludesUnrealizedPnlAndTracksPeakDrawdown()
    {
        var startedAt = new DateTime(2025, 1, 1);
        var state = new BacktestPortfolioState(1_000m, startedAt);
        state.OpenPositions["AAA"] = new BacktestExecutionAdapter.OpenPosition
        {
            EntryPrice = 100m,
            Quantity = 10,
            CurrentQuantity = 10
        };

        var rise = startedAt.AddDays(1);
        state.UpdateLatestPrices(rise, Prepared("AAA", rise, 110m));
        state.RecordMarkedEquity(rise);

        var fall = startedAt.AddDays(2);
        state.UpdateLatestPrices(fall, Prepared("AAA", fall, 90m));
        state.RecordMarkedEquity(fall);

        state.EquityCurve.Should().HaveCount(3);
        state.EquityCurve[1].Equity.Should().Be(1_100m);
        state.EquityCurve[2].Equity.Should().Be(900m);
        state.MaxDrawdown.Should().BeApproximately(200m / 1_100m, 0.0000001m);
    }

    [Fact]
    public void DailyLossLimit_UsesEquityAtStartOfEachTradingDay()
    {
        var state = new BacktestPortfolioState(1_000m, new DateTime(2025, 1, 1));
        var firstDay = new DateOnly(2025, 1, 2);
        state.BeginTradingDay(firstDay);
        state.ApplyRealizedTrade(new TradeRecord { PnL = -100m });

        state.HasReachedDailyLossLimit(0.10m).Should().BeTrue();
        state.BeginTradingDay(firstDay);
        state.HasReachedDailyLossLimit(0.10m).Should().BeTrue();

        state.BeginTradingDay(firstDay.AddDays(1));
        state.HasReachedDailyLossLimit(0.10m).Should().BeFalse();
    }

    [Fact]
    public void RecordMarkedEquity_ReplacesDuplicateTimestamp()
    {
        var timestamp = new DateTime(2025, 1, 1);
        var state = new BacktestPortfolioState(1_000m, timestamp);

        state.RecordMarkedEquity(timestamp);

        state.EquityCurve.Should().ContainSingle();
    }

    private static IReadOnlyDictionary<string, PreparedSymbolData> Prepared(
        string symbol,
        DateTime timestamp,
        decimal close)
    {
        var bar = new OhlcvBar { Symbol = symbol, Timestamp = timestamp, Close = close };
        return new Dictionary<string, PreparedSymbolData>
        {
            [symbol] = new(
                [bar], [0m], [close], [0m], [0m], [0m],
                new Dictionary<DateTime, int> { [timestamp] = 0 })
        };
    }
}
