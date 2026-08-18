using FluentAssertions;
using StockTrader.Application.Execution;

namespace StockTrader.Tests;

public class LiveLongPositionExecutionAdapterTests
{
    private static readonly LongPositionExitPolicy Policy = new(
        10, true, 2m, 1m, false, 0m, true, true);

    [Fact]
    public void Evaluate_UsesExistingStopBeforeAnyUpdate()
    {
        var result = Evaluate(State(), 94m, Policy);

        result.ShouldExit.Should().BeTrue();
        result.Reason.Should().Be("손절");
        result.Intent!.Quantity.Should().Be(10);
    }

    [Fact]
    public void Evaluate_TargetWinsOverStrategyAndTimeExit()
    {
        var result = LiveLongPositionExecutionAdapter.Evaluate(
            State(), 10, 115m, 2m, Policy, true,
            new StrategyExitInstruction(115m, "전략 청산"));

        result.ShouldExit.Should().BeTrue();
        result.Reason.Should().Be("목표 도달");
    }

    [Fact]
    public void Evaluate_ZeroMaximumHoldingBarsDisablesTimeExit()
    {
        var result = LiveLongPositionExecutionAdapter.Evaluate(
            State(), 10, 102m, 2m, Policy with { MaxHoldingBars = 0 }, true);

        result.ShouldExit.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_UsesCommonSessionForProtectiveStopCalculation()
    {
        var live = Evaluate(State(), 110m, Policy);

        live.ShouldExit.Should().BeFalse();
        live.State.TrailingActivated.Should().BeTrue();
        live.State.StopPrice.Should().Be(106m);
        live.StopUpdate.Should().NotBeNull();
    }

    [Fact]
    public void Evaluate_EmitsPartialIntentWithoutApplyingFillBeforeBrokerConfirmation()
    {
        var result = Evaluate(
            State(),
            105m,
            Policy with
            {
                EnablePartialProfit = true,
                PartialProfitRMultiple = 1m,
                EnableTargetExit = false,
            });

        result.Intent.Should().Be(new LiveLongPositionExecutionIntent(
            5, "부분 익절(1R)", MarksPartialProfit: true));
        result.State.CurrentQuantity.Should().Be(10);
        result.State.PartialProfitTaken.Should().BeFalse();
        result.State.StopPrice.Should().Be(95m);
        result.State.BreakevenApplied.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DefersSameSnapshotFullExitUntilPartialFillIsConfirmed()
    {
        var result = Evaluate(
            State(),
            115m,
            Policy with { EnablePartialProfit = true, PartialProfitRMultiple = 1m });

        result.Intent!.MarksPartialProfit.Should().BeTrue();
        result.Intent.Quantity.Should().Be(5);
        result.State.CurrentQuantity.Should().Be(10);
    }

    [Fact]
    public void Evaluate_EmitsRemainingFullExitAfterPartialFillIsConfirmed()
    {
        var afterPartialFill = State() with
        {
            CurrentQuantity = 5,
            PartialProfitTaken = true,
            StopPrice = 100m,
            BreakevenApplied = true,
        };

        var result = LiveLongPositionExecutionAdapter.Evaluate(
            afterPartialFill,
            initialQuantity: 10,
            currentPrice: 115m,
            currentAtr: 2m,
            Policy with { EnablePartialProfit = true, PartialProfitRMultiple = 1m },
            timeExitReached: false);

        result.Intent.Should().Be(new LiveLongPositionExecutionIntent(
            5, "목표 도달", MarksPartialProfit: false));
    }

    private static LiveLongPositionExecutionDecision Evaluate(
        LongPositionExecutionState state,
        decimal currentPrice,
        LongPositionExitPolicy policy) =>
        LiveLongPositionExecutionAdapter.Evaluate(
            state, 10, currentPrice, 2m, policy, timeExitReached: false);

    private static LongPositionExecutionState State() => new(
        100m, 95m, 115m, 100m, 100m, 5m, 2m, 0, 10);
}
