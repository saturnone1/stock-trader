namespace StockTrader.Application.Signals;

public enum SignalFreshnessStatus
{
    Actionable,
    Expired,
    FutureDated,
}

public sealed record SignalFreshnessWindow(
    DateTime DetectedFromInclusiveUtc,
    DateTime DetectedThroughInclusiveUtc);

/// <summary>
/// Owns the common observation-time rule used by signal browsing, dashboard counts,
/// and manual entry. Provider bar timestamps remain event identity; DetectedAt is the
/// application observation timestamp used for operational lifetime.
/// </summary>
public sealed class SignalFreshnessPolicy
{
    public static readonly TimeSpan MaximumConfigurableLifetime = TimeSpan.FromDays(7);

    public SignalFreshnessPolicy(TimeSpan actionableLifetime)
    {
        if (actionableLifetime <= TimeSpan.Zero
            || actionableLifetime > MaximumConfigurableLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(actionableLifetime),
                $"Signal lifetime must be greater than zero and no longer than "
                + $"{MaximumConfigurableLifetime.TotalHours:F0} hours.");
        }

        ActionableLifetime = actionableLifetime;
    }

    public TimeSpan ActionableLifetime { get; }

    public SignalFreshnessWindow GetWindow(DateTime observedAtUtc) => new(
        observedAtUtc - ActionableLifetime,
        observedAtUtc);

    public SignalFreshnessStatus Evaluate(DateTime detectedAtUtc, DateTime observedAtUtc)
    {
        if (detectedAtUtc > observedAtUtc)
            return SignalFreshnessStatus.FutureDated;

        return detectedAtUtc < observedAtUtc - ActionableLifetime
            ? SignalFreshnessStatus.Expired
            : SignalFreshnessStatus.Actionable;
    }

    public bool IsActionable(DateTime detectedAtUtc, DateTime observedAtUtc) =>
        Evaluate(detectedAtUtc, observedAtUtc) == SignalFreshnessStatus.Actionable;
}
