using FluentAssertions;
using StockTrader.Application.Execution;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Tests;

public class LongPositionExitPolicyCatalogTests
{
    [Fact]
    public void ForCustom_MapsStrategyExitSettingsWithoutAdapterDefaults()
    {
        var definition = new CustomPatternDefinition
        {
            MaxHoldingBars = 12,
            TrailingAtr = 2.25m,
            PartialProfitR = 1.75m
        };

        var policy = LongPositionExitPolicyCatalog.ForCustom(definition);

        policy.Should().Be(new LongPositionExitPolicy(
            MaxHoldingBars: 12,
            EnableTrailingStop: true,
            TrailingStopAtrMultiplier: 2.25m,
            TrailingActivationR: 1m,
            EnablePartialProfit: true,
            PartialProfitRMultiple: 1.75m,
            EnableTargetExit: true,
            EnableTimeExit: true));
    }

    [Fact]
    public void ForCustom_ZeroValuesDisableOptionalExits()
    {
        var policy = LongPositionExitPolicyCatalog.ForCustom(new CustomPatternDefinition
        {
            MaxHoldingBars = 0,
            TrailingAtr = 0m,
            PartialProfitR = 0m
        });

        policy.EnableTrailingStop.Should().BeFalse();
        policy.EnablePartialProfit.Should().BeFalse();
        policy.EnableTimeExit.Should().BeTrue(
            "시간 청산 활성 여부와 0봉 무제한 의미는 실행 정책에서 별도로 해석합니다");
    }

    [Theory]
    [MemberData(nameof(BuiltInPolicies))]
    public void ForPattern_PreservesEveryBuiltInBaseline(
        PatternType patternType,
        LongPositionExitPolicy expected)
    {
        var policy = LongPositionExitPolicyCatalog.ForPattern(patternType);

        policy.Should().Be(expected);
    }

    [Fact]
    public void ForPattern_AppliesExplicitZeroOverridesAsDisableRequests()
    {
        var policy = LongPositionExitPolicyCatalog.ForPattern(
            PatternType.Breakout,
            new PatternParameterOverrides
            {
                Breakout_ExitMaxHoldingBars = 8,
                Breakout_ExitTrailingAtr = 0m,
                Breakout_ExitPartialR = 0m
            });

        policy.MaxHoldingBars.Should().Be(8);
        policy.EnableTrailingStop.Should().BeFalse();
        policy.TrailingStopAtrMultiplier.Should().Be(0m);
        policy.EnablePartialProfit.Should().BeFalse();
        policy.PartialProfitRMultiple.Should().Be(0m);
    }

    public static TheoryData<PatternType, LongPositionExitPolicy> BuiltInPolicies() => new()
    {
        { PatternType.GapUpPullback, Policy(3, false, 0m, 0m, true, 2.0m) },
        { PatternType.VwapReversion, Policy(3, false, 0m, 0m, true, 1.5m) },
        { PatternType.OpeningRangeBreakout, Policy(3, false, 0m, 0m, true, 2.0m) },
        { PatternType.VolumeSpikeContinuation, Policy(5, true, 1.5m, 1.0m, false, 0m) },
        { PatternType.VolatilityBreakout, Policy(5, true, 2.0m, 1.0m, false, 0m) },
        { PatternType.RsiMeanReversion, Policy(5, false, 0m, 0m, true, 1.5m) },
        { PatternType.VolatilityExpansion, Policy(7, true, 2.0m, 1.5m, true, 2.0m) },
        { PatternType.MeanReversionChannel, Policy(5, false, 0m, 0m, true, 1.5m) },
        { PatternType.Rsi2Bollinger, Policy(5, false, 0m, 0m, true, 1.5m) },
        { PatternType.CumulativeRsi2, Policy(20, false, 0m, 0m, false, 0m, false, false, 0m) },
        { PatternType.Breakout, Policy(15, true, 2.5m, 1.5m, true, 2.5m) },
        { PatternType.MomentumReversal, Policy(10, true, 2.5m, 1.5m, true, 2.0m) },
        { PatternType.IndexRegimeFilter, Policy(15, true, 2.5m, 1.5m, true, 2.0m) },
        { PatternType.TrendPullback, Policy(20, true, 3.0m, 2.0m, true, 3.0m) },
        { PatternType.EarningsDrift, Policy(20, true, 2.5m, 1.5m, true, 2.0m) },
        { PatternType.MultiTimeframeTrend, Policy(30, true, 3.0m, 2.0m, true, 3.0m) },
        { PatternType.Tqqq200Sma, Policy(999, false, 0m, 0m, false, 0m, false, false) }
    };

    private static LongPositionExitPolicy Policy(
        int maxHoldingBars,
        bool trailing,
        decimal trailingAtr,
        decimal trailingActivationR,
        bool partialProfit,
        decimal partialProfitR,
        bool target = true,
        bool time = true,
        decimal breakevenAtr = 1.5m) => new(
            maxHoldingBars,
            trailing,
            trailingAtr,
            trailingActivationR,
            partialProfit,
            partialProfitR,
            target,
            time,
            breakevenAtr);
}
