using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

/// <summary>
/// 신호가 유효한 목표가를 싣지 못했을 때 쓰는 폴백 R 배수가 전략 기하에서 도출되며,
/// preview 와 backtest 가 같은 값을 얻는지 고정한다.
/// 이전에는 preview 가 전략 배수 비율을, backtest 의 차기봉 체결이 상수 2를 사용해
/// 같은 신호에서 서로 다른 목표가를 만들었다.
/// </summary>
public sealed class LongEntryFillFallbackTargetTests
{
    [Theory]
    [InlineData(2, 3, 1.5)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 1, 0.5)]
    [InlineData(1.5, 4.5, 3)]
    public void FallbackFollowsTheStrategysDeclaredRiskRewardGeometry(
        decimal stopMultiplier, decimal targetMultiplier, decimal expected)
    {
        LongEntryFillPolicy
            .ResolveFallbackTargetMultiple(stopMultiplier, targetMultiplier)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 0)]
    [InlineData(-1, 3)]
    [InlineData(2, -1)]
    public void UnusableMultipliersFallBackToOneRatherThanAnInventedRatio(
        decimal stopMultiplier, decimal targetMultiplier)
    {
        LongEntryFillPolicy
            .ResolveFallbackTargetMultiple(stopMultiplier, targetMultiplier)
            .Should().Be(1m);
    }

    [Fact]
    public void DegenerateSignalTargetUsesTheStrategyGeometryNotAFixedTwoR()
    {
        // 손절 2 ATR, 목표 3 ATR 전략 → 손익비 1.5R.
        var fallback = LongEntryFillPolicy.ResolveFallbackTargetMultiple(2m, 3m);

        // 신호 목표가가 진입가 이하라 사용할 수 없는 경우.
        var fill = LongEntryFillPolicy.Reprice(
            signalEntry: 100m,
            signalStop: 90m,
            signalTarget: 95m,
            actualEntry: 100m,
            fallbackTargetMultiple: fallback);

        fill.Should().NotBeNull();
        fill!.RiskDistance.Should().Be(10m);
        fill.StopPrice.Should().Be(90m);
        // 1.5R → 100 + 10*1.5 = 115. 상수 2R 이었다면 120 이 되었을 것이다.
        fill.TargetPrice.Should().Be(115m);
        fill.TargetPrice.Should().NotBe(120m);
    }

    [Fact]
    public void UsableSignalTargetIsPreservedAndTheFallbackIsIgnored()
    {
        var fill = LongEntryFillPolicy.Reprice(
            signalEntry: 100m,
            signalStop: 90m,
            signalTarget: 130m,
            actualEntry: 105m,
            fallbackTargetMultiple: LongEntryFillPolicy
                .ResolveFallbackTargetMultiple(2m, 3m));

        // 신호가 3R 을 실었으므로 폴백(1.5R)이 아니라 3R 이 유지된다.
        fill.Should().NotBeNull();
        fill!.TargetPrice.Should().Be(105m + 10m * 3m);
    }
}
