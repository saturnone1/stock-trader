using FluentAssertions;
using StockTrader.Domain.MarketData;

namespace StockTrader.Tests;

/// <summary>
/// 조정 모드 카탈로그가 각 어댑터의 실제 요청/후처리와 일치하는지 고정한다.
/// 어댑터가 요청 파라미터를 바꾸면 이 테스트가 먼저 깨져야 한다.
/// </summary>
public sealed class PriceAdjustmentCatalogTests
{
    [Theory]
    [InlineData(TimeFrame.OneMinute)]
    [InlineData(TimeFrame.FiveMinute)]
    [InlineData(TimeFrame.FifteenMinute)]
    [InlineData(TimeFrame.Daily)]
    [InlineData(TimeFrame.Weekly)]
    public void Alpaca_RequestsSplitAndDividendAdjustedSeriesOnEveryTimeFrame(TimeFrame timeFrame)
    {
        // AlpacaDataFeedService 는 과거·분봉 요청 모두에 Adjustment.SplitsAndDividends 를 설정한다.
        PriceAdjustmentCatalog.Resolve(DataSource.Alpaca, timeFrame)
            .Should().Be(PriceAdjustmentMode.SplitsAndDividends);
    }

    [Theory]
    [InlineData(TimeFrame.OneMinute)]
    [InlineData(TimeFrame.FiveMinute)]
    [InlineData(TimeFrame.FifteenMinute)]
    [InlineData(TimeFrame.Daily)]
    [InlineData(TimeFrame.Weekly)]
    public void Yahoo_NormalizesEveryTimeFrameToAnAdjustedSeries(TimeFrame timeFrame)
    {
        // YahooFinanceDataFeedService 는 adjclose/close 비율을 OHLC 에 적용하므로 원본 계열 경로가 없다.
        PriceAdjustmentCatalog.Resolve(DataSource.Yahoo, timeFrame)
            .Should().Be(PriceAdjustmentMode.SplitsAndDividends);
    }

    [Theory]
    [InlineData(TimeFrame.Daily)]
    [InlineData(TimeFrame.Weekly)]
    public void LsSecurities_RequestsAdjustedPricesForDailyAndWeekly(TimeFrame timeFrame)
    {
        // t8410 은 sujung="Y" 로 수정주가를 요청한다.
        PriceAdjustmentCatalog.Resolve(DataSource.LsSecurities, timeFrame)
            .Should().Be(PriceAdjustmentMode.SplitsAndDividends);
    }

    [Theory]
    [InlineData(TimeFrame.OneMinute)]
    [InlineData(TimeFrame.FiveMinute)]
    [InlineData(TimeFrame.FifteenMinute)]
    public void LsSecurities_ReturnsUnadjustedIntradayBecauseTheMinuteTrHasNoAdjustmentField(
        TimeFrame timeFrame)
    {
        // t8412 요청 본문에는 sujung 에 해당하는 필드가 없어 원본 체결가가 반환된다.
        // 같은 공급자 안에서 타임프레임에 따라 조정 여부가 달라지는 사실을 명시적으로 고정한다.
        PriceAdjustmentCatalog.Resolve(DataSource.LsSecurities, timeFrame)
            .Should().Be(PriceAdjustmentMode.Unadjusted);
    }

    [Fact]
    public void EveryImplementedProviderDeclaresAnAdjustmentModeForEverySupportedTimeFrame()
    {
        foreach (var provider in DataProviderCatalog.Implemented)
        {
            foreach (var timeFrame in provider.SupportedTimeFrames)
            {
                var resolve = () => PriceAdjustmentCatalog.Resolve(provider.Value, timeFrame);

                resolve.Should().NotThrow(
                    $"{provider.DisplayName} 의 {timeFrame} 조정 모드가 선언되어야 합니다");
            }
        }
    }
}
