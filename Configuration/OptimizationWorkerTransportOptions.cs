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
    public const int MinimumRemotePollMilliseconds = 250;
    public const int MaximumRemotePollMilliseconds = 10_000;
    public const int MaximumConcurrentRemoteJobs = 16;

    public bool Enabled { get; init; }
    public bool LeaseTransportEnabled { get; init; }
    public OptimizationWorkerTransportMode Mode { get; init; } =
        OptimizationWorkerTransportMode.Shadow;
    public string SharedSecret { get; init; } = string.Empty;
    public int LeaseSeconds { get; init; } = 300;
    public int RemotePollMilliseconds { get; init; } = 1_000;
    public int MaxConcurrentRemoteJobs { get; init; } = 2;
    public string ClientCertificateAuthorityPath { get; init; } = string.Empty;
    public string ClientCertificateCommonName { get; init; } =
        "stocktrader-optimization-worker";

    public bool IsValid() =>
        LeaseSeconds is >= MinimumLeaseSeconds and <= MaximumLeaseSeconds
        && RemotePollMilliseconds is >= MinimumRemotePollMilliseconds
            and <= MaximumRemotePollMilliseconds
        && MaxConcurrentRemoteJobs is >= 1 and <= MaximumConcurrentRemoteJobs
        && (!Enabled || SharedSecret.Length >= MinimumSecretLength)
        && (!LeaseTransportEnabled || Enabled
            && !string.IsNullOrWhiteSpace(ClientCertificateAuthorityPath)
            && !string.IsNullOrWhiteSpace(ClientCertificateCommonName))
        && (Mode != OptimizationWorkerTransportMode.Remote || LeaseTransportEnabled);
}
