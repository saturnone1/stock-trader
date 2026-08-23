namespace StockTrader.Configuration;

public enum OptimizationWorkerTransportMode
{
    Shadow,
    Remote
}

public sealed class OptimizationWorkerTransportOptions
{
    public const string SectionName = "OptimizationWorkerTransport";
    public const int MinimumSecretLength = 32;
    public const int MinimumLeaseSeconds = 30;
    public const int MaximumLeaseSeconds = 1800;

    public bool Enabled { get; init; }
    public bool LeaseTransportEnabled { get; init; }
    public OptimizationWorkerTransportMode Mode { get; init; } =
        OptimizationWorkerTransportMode.Shadow;
    public string SharedSecret { get; init; } = string.Empty;
    public int LeaseSeconds { get; init; } = 300;

    public bool IsValid() =>
        LeaseSeconds is >= MinimumLeaseSeconds and <= MaximumLeaseSeconds
        && (!Enabled || SharedSecret.Length >= MinimumSecretLength)
        && (!LeaseTransportEnabled || Enabled);
}
