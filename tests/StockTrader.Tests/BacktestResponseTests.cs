using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class BacktestResponseTests
{
    [Fact]
    public void Create_ExposesRiskMetricsAndCostAdjustedTradeReturnWithIntradayTime()
    {
        var result = new BacktestResult
        {
            TotalTrades = 1,
            TotalReturnPercent = 0.08m,
            MaxDrawdown = 0.04m,
            SharpeRatio = 1.2m,
            SortinoRatio = 1.8m,
            CalmarRatio = 2m,
            ProfitFactor = 1.6m,
            AnnualizedReturn = 12.5m,
            OverallWinRate = 1m,
            KellyFraction = 0.20m,
            HalfKellyFraction = 0.10m,
            AvgMaePercent = -0.02m,
            AvgMfePercent = 0.05m,
            MedianMaePercent = -0.01m,
            MedianMfePercent = 0.04m,
            UsedTimeFrame = TimeFrame.OneMinute,
            Trades =
            [
                new TradeRecord
                {
                    Symbol = "TQQQ",
                    PatternType = PatternType.Custom,
                    EntryTime = new DateTime(2026, 8, 18, 9, 31, 0, DateTimeKind.Utc),
                    ExitTime = new DateTime(2026, 8, 18, 9, 34, 0, DateTimeKind.Utc),
                    EntryPrice = 100m,
                    ExitPrice = 110m,
                    Quantity = 10,
                    PnL = 97m,
                    PnLPercent = 0.097m,
                    ExitReason = "목표 도달"
                }
            ],
            PerRegimeStats = new Dictionary<string, RegimePerformance>
            {
                ["Bull"] = new()
                {
                    TradeCount = 1,
                    WinRate = 1m,
                    TotalPnL = 97m,
                    AverageTradeReturn = 0.097m,
                    ProfitFactor = 99.9m
                }
            }
        };

        var response = BacktestResponse.Create(result);

        response.AnnualizedReturn.Should().Be(12.5m);
        response.SortinoRatio.Should().Be(1.8m);
        response.HalfKellyFraction.Should().Be(0.10m);
        response.PerRegimeStats["Bull"].TotalPnL.Should().Be(97m);
        response.Trades.Should().ContainSingle();
        response.Trades[0].ReturnPct.Should().Be(0.097m,
            "the API must expose cost-adjusted PnLPercent, not raw exit-price return");
        response.Trades[0].NetPnL.Should().Be(97m);
        response.Trades[0].EntryTime.Should().Contain("T09:31:00");
    }

    [Fact]
    public void Create_DownsamplesEquityWithoutLosingIntradayBoundaries()
    {
        var from = new DateTime(2026, 8, 18, 9, 30, 0, DateTimeKind.Utc);
        var result = new BacktestResult
        {
            EquityCurve = Enumerable.Range(0, 600)
                .Select(index => new EquityPoint(from.AddMinutes(index), 100_000m + index))
                .ToList()
        };

        var response = BacktestResponse.Create(result);

        response.EquityCurve.Should().HaveCount(300);
        response.EquityCurve[0].Timestamp.Should().Contain("T09:30:00");
        response.EquityCurve[^1].Timestamp.Should().Contain("T19:29:00");
        response.EquityCurve[^1].Equity.Should().Be(100_599m);
    }
}
