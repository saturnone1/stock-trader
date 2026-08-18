namespace StockTrader.Configuration;

/// <summary>
/// Controls how long a detected signal remains eligible for operator views and entry execution.
/// Values are operational configuration; signal-time comparisons remain deterministic policy.
/// </summary>
public sealed class SignalLifecycleOptions
{
    public const string SectionName = "SignalLifecycle";

    public double ActionableLifetimeHours { get; set; }
}
