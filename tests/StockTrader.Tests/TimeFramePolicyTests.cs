using FluentAssertions;
using StockTrader.Application.Backtesting;
using StockTrader.Application.StrategyPreview;
using StockTrader.Domain.MarketData;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class TimeFramePolicyTests
{
    [Fact]
    public void CatalogCoversEveryTimeFrame()
    {
        TimeFrameCatalog.All.Select(item => item.Value)
            .Should().BeEquivalentTo(Enum.GetValues<TimeFrame>());
    }

    [Theory]
    [InlineData(TimeFrame.OneMinute, true, "1분봉", 98280)]
    [InlineData(TimeFrame.FiveMinute, true, "5분봉", 19656)]
    [InlineData(TimeFrame.FifteenMinute, true, "15분봉", 6552)]
    [InlineData(TimeFrame.Daily, false, "일봉", 252)]
    [InlineData(TimeFrame.Weekly, false, "주봉", 52)]
    public void CatalogExposesStableFacts(
        TimeFrame timeFrame, bool intraday, string displayName, int annualizationPeriods)
    {
        var descriptor = TimeFrameCatalog.Get(timeFrame);

        descriptor.IsIntraday.Should().Be(intraday);
        descriptor.DisplayName.Should().Be(displayName);
        descriptor.AnnualizationPeriods.Should().Be(annualizationPeriods);
    }

    [Fact]
    public void FeaturePoliciesCoverEveryTimeFrame()
    {
        foreach (var timeFrame in Enum.GetValues<TimeFrame>())
        {
            BacktestTimeFramePolicy.Get(timeFrame).WarmupCalendarDays.Should().BePositive();
            BacktestTimeFramePolicy.Get(timeFrame).SimulationWindowBars.Should().BePositive();
            PreviewTimeFramePolicy.Get(timeFrame).MaximumRange.Should().BePositive();
            PreviewTimeFramePolicy.Get(timeFrame).WarmupRange.Should().BePositive();
            PreviewTimeFramePolicy.Get(timeFrame).CoverageTolerance.Should().BePositive();
        }
    }

    [Fact]
    public void WeeklyPoliciesRetainFiveYearsOfWarmup()
    {
        BacktestTimeFramePolicy.Get(TimeFrame.Weekly).WarmupCalendarDays.Should().Be(365 * 5);
        PreviewTimeFramePolicy.Get(TimeFrame.Weekly).WarmupRange.Should().Be(TimeSpan.FromDays(365 * 5));
    }
}
