using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.MarketData;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class MarketDataEvidenceTests
{
    [Fact]
    public void Create_AssemblesMarketAndTimeZoneFromTheCatalogsRatherThanTheCaller()
    {
        var evidence = MarketDataEvidence.Create(
            DataSource.Alpaca,
            TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly,
            warmupCalendarDays: 400,
            requiredWarmupBars: 50);

        evidence.MarketRegion.Should().Be(MarketRegion.UnitedStates);
        evidence.MarketTimeZoneId.Should().Be("America/New_York");
        evidence.AdjustmentMode.Should().Be(PriceAdjustmentMode.SplitsAndDividends);
        evidence.CalendarVersion.Should().Be(MarketCalendarVersion.Current);
        evidence.WarmupCalendarDays.Should().Be(400);
        evidence.RequiredWarmupBars.Should().Be(50);
    }

    [Fact]
    public void Create_CarriesTheKoreanMarketIdentityForItsProvider()
    {
        var evidence = MarketDataEvidence.Create(
            DataSource.LsSecurities,
            TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly,
            warmupCalendarDays: 400,
            requiredWarmupBars: 50);

        evidence.MarketRegion.Should().Be(MarketRegion.Korea);
        evidence.MarketTimeZoneId.Should().Be("Asia/Seoul");
    }

    [Fact]
    public void ResultsFromDifferentAdjustmentModesAreNotComparable()
    {
        // 같은 공급자·같은 종목이라도 LS증권 일봉(수정주가)과 분봉(원본가)은
        // 서로 다른 가격 계열이므로 수익률을 직접 비교할 수 없다.
        var adjusted = MarketDataEvidence.Create(
            DataSource.LsSecurities, TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly, 400, 50);
        var unadjusted = MarketDataEvidence.Create(
            DataSource.LsSecurities, TimeFrame.FiveMinute,
            MarketSessionScope.RegularSessionOnly, 400, 50);

        adjusted.AdjustmentMode.Should().Be(PriceAdjustmentMode.SplitsAndDividends);
        unadjusted.AdjustmentMode.Should().Be(PriceAdjustmentMode.Unadjusted);
        adjusted.IsComparableTo(unadjusted).Should().BeFalse();
    }

    [Fact]
    public void ResultsFromTheSameConditionsAreComparable()
    {
        var first = MarketDataEvidence.Create(
            DataSource.Alpaca, TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly, 400, 50);
        var second = MarketDataEvidence.Create(
            DataSource.Alpaca, TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly, 400, 50);

        first.IsComparableTo(second).Should().BeTrue();
    }

    [Fact]
    public void ResultsFromDifferentSessionScopesAreNotComparable()
    {
        var regular = MarketDataEvidence.Create(
            DataSource.Alpaca, TimeFrame.Daily,
            MarketSessionScope.RegularSessionOnly, 400, 50);
        var extended = MarketDataEvidence.Create(
            DataSource.Alpaca, TimeFrame.Daily,
            MarketSessionScope.IncludingExtendedHours, 400, 50);

        regular.IsComparableTo(extended).Should().BeFalse();
    }

    [Fact]
    public void BacktestResponsePreservesEveryEvidenceFieldForStoredResults()
    {
        var evidence = MarketDataEvidence.Create(
            DataSource.Yahoo,
            TimeFrame.Weekly,
            MarketSessionScope.RegularSessionOnly,
            warmupCalendarDays: 1825,
            requiredWarmupBars: 50);
        var result = new BacktestResult { DataEvidence = evidence };

        var response = BacktestResponse.Create(result);

        response.DataEvidence.Should().NotBeNull();
        response.DataEvidence!.Provider.Should().Be("Yahoo");
        response.DataEvidence.MarketRegion.Should().Be("UnitedStates");
        response.DataEvidence.MarketTimeZoneId.Should().Be("America/New_York");
        response.DataEvidence.TimeFrame.Should().Be("Weekly");
        response.DataEvidence.AdjustmentMode.Should().Be("SplitsAndDividends");
        response.DataEvidence.SessionScope.Should().Be("RegularSessionOnly");
        response.DataEvidence.CalendarVersion.Should().Be(MarketCalendarVersion.Current);
        response.DataEvidence.WarmupCalendarDays.Should().Be(1825);
        response.DataEvidence.RequiredWarmupBars.Should().Be(50);
    }

    [Fact]
    public void BacktestResponseTolerateAResultWithoutEvidence()
    {
        // 근거를 붙이지 않은 실패 결과(데이터 없음 등)도 계약을 깨뜨리지 않아야 한다.
        var response = BacktestResponse.Create(new BacktestResult());

        response.DataEvidence.Should().BeNull();
    }
}
