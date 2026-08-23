using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace StockTrader.ServiceContracts.MachineLearning;

public static class MlTrainingContractVersions
{
    public const int Current = 1;
    public const int Trainer = 1;
    public const int RegimeFeatureSchema = 1;
    public const int SignalFeatureSchema = 1;
    public const int RegimeFeatureCount = 7;
    public const int SignalFeatureCount = 10;
    public const int RequiredRegimeClusters = 4;
}

public static class MlModelKinds
{
    public const string MarketRegime = "MarketRegime";
    public const string SignalScorer = "SignalScorer";
}

public static class MlRegimeLabels
{
    public const string Bullish = "강세장";
    public const string Bearish = "약세장";
    public const string Sideways = "횡보장";
    public const string HighVolatility = "고변동장";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Bullish, Bearish, Sideways, HighVolatility], StringComparer.Ordinal);
}

public static class MlTrainingJobStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string PartiallyCompleted = "PartiallyCompleted";
    public const string InsufficientData = "InsufficientData";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public sealed record MlRegimeDataEvidenceContract(
    string Provider,
    string Symbol,
    string TimeFrame,
    string AdjustmentMode,
    string CalendarVersion,
    DateTime FromUtc,
    DateTime ToUtc,
    string ContentHash,
    string EvidenceId);

public sealed record MlRegimeFeatureContract(
    DateTime AsOfUtc,
    float Return5Day,
    float Return10Day,
    float Return20Day,
    float VolatilityLevel,
    float VolumeChangeRate,
    float MaSlopePercent,
    float Rsi);

public sealed record MlSignalFeatureContract(
    int SchemaVersion,
    float PatternTypeCode,
    float Rsi,
    float BollingerPosition,
    float VolumeRatio,
    float MarketRegimeCode,
    float AtrPercent,
    float HistoricalWinRate,
    float RiskRewardRatio,
    float PriceVsLongMovingAverage,
    float LongTrendHistoryAvailable);

public sealed record MlSignalTrainingSampleContract(
    long SourceSignalId,
    DateTime SignalBarAtUtc,
    MlSignalFeatureContract Features,
    bool IsWin);

public sealed record MlTrainingJobRequest(
    int ContractVersion,
    string JobId,
    string InputHash,
    int TrainerVersion,
    int RegimeFeatureSchemaVersion,
    int SignalFeatureSchemaVersion,
    DateTime RequestedAtUtc,
    DateTime ObservationCutoffUtc,
    int MinimumTrainingSamples,
    int RegimeClusterCount,
    MlRegimeDataEvidenceContract RegimeEvidence,
    IReadOnlyList<MlRegimeFeatureContract> RegimeSamples,
    IReadOnlyList<MlSignalTrainingSampleContract> SignalSamples);

public sealed record MlFeatureImportanceContract(
    string FeatureName,
    double Importance);

public sealed record MlModelArtifactContract(
    int ContractVersion,
    string ArtifactId,
    string ModelKind,
    int TrainerVersion,
    int FeatureSchemaVersion,
    int FeatureCount,
    DateTime TrainedAtUtc,
    DateTime TrainingCutoffUtc,
    int TrainingSamples,
    double? ValidationAccuracy,
    double? ValidationAuc,
    IReadOnlyDictionary<uint, string>? ClusterLabels,
    IReadOnlyList<MlFeatureImportanceContract> FeatureImportances,
    string ModelSha256,
    byte[] ModelBytes)
{
    public MlModelArtifactContract MetadataOnly() => this with { ModelBytes = [] };
}

public sealed record MlTrainingJobResult(
    int ContractVersion,
    string JobId,
    string InputHash,
    string Status,
    string Message,
    long PublicationRevision,
    DateTime AcceptedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    long DurationMilliseconds,
    MlModelArtifactContract? RegimeArtifact,
    MlModelArtifactContract? SignalArtifact,
    bool AlreadyAccepted);

public sealed record MlTrainingJobAccepted(
    string JobId,
    string InputHash,
    string Status,
    bool AlreadyAccepted);

public sealed record MlTrainingServiceStatus(
    int ContractVersion,
    bool Ready,
    bool DatabaseReady,
    long PublicationRevision,
    int PendingJobs,
    int RunningJobs,
    int CompletedJobs,
    DateTime? LastCompletedAtUtc,
    string? LastError);

public sealed record MlTrainingPublicationSnapshot(
    int ContractVersion,
    long PublicationRevision,
    MlModelArtifactContract? RegimeArtifact,
    MlModelArtifactContract? SignalArtifact);

public static class MlTrainingContractHash
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Sha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public static string Sha256(string value) =>
        Sha256(Encoding.UTF8.GetBytes(value));

    public static string Input(MlTrainingJobRequest request) => Sha256(string.Join('\n',
        Header(request),
        RegimeEvidence(request.RegimeEvidence),
        string.Join('\n', request.RegimeSamples
            .OrderBy(sample => Utc(sample.AsOfUtc))
            .Select(RegimeSample)),
        string.Join('\n', request.SignalSamples
            .OrderBy(sample => Utc(sample.SignalBarAtUtc))
            .ThenBy(sample => sample.SourceSignalId)
            .Select(SignalSample))));

    public static string Artifact(MlModelArtifactContract artifact) => Sha256(string.Join('|',
        artifact.ContractVersion,
        artifact.ModelKind,
        artifact.TrainerVersion,
        artifact.FeatureSchemaVersion,
        artifact.FeatureCount,
        Utc(artifact.TrainedAtUtc).ToString("O", Invariant),
        Utc(artifact.TrainingCutoffUtc).ToString("O", Invariant),
        artifact.TrainingSamples,
        Number(artifact.ValidationAccuracy),
        Number(artifact.ValidationAuc),
        Labels(artifact.ClusterLabels),
        Importances(artifact.FeatureImportances),
        artifact.ModelSha256));

    public static string RegimeEvidence(MlRegimeDataEvidenceContract evidence) =>
        string.Join('|', evidence.Provider, evidence.Symbol, evidence.TimeFrame,
            evidence.AdjustmentMode, evidence.CalendarVersion,
            Utc(evidence.FromUtc).ToString("O", Invariant),
            Utc(evidence.ToUtc).ToString("O", Invariant), evidence.ContentHash,
            evidence.EvidenceId);

    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static string Header(MlTrainingJobRequest request) => string.Join('|',
        request.ContractVersion, request.TrainerVersion,
        request.RegimeFeatureSchemaVersion, request.SignalFeatureSchemaVersion,
        Utc(request.ObservationCutoffUtc).ToString("O", Invariant),
        request.MinimumTrainingSamples, request.RegimeClusterCount);

    private static string RegimeSample(MlRegimeFeatureContract sample) => string.Join('|',
        Utc(sample.AsOfUtc).ToString("O", Invariant),
        Float(sample.Return5Day), Float(sample.Return10Day), Float(sample.Return20Day),
        Float(sample.VolatilityLevel), Float(sample.VolumeChangeRate),
        Float(sample.MaSlopePercent), Float(sample.Rsi));

    private static string SignalSample(MlSignalTrainingSampleContract sample) => string.Join('|',
        sample.SourceSignalId,
        Utc(sample.SignalBarAtUtc).ToString("O", Invariant),
        sample.Features.SchemaVersion,
        Float(sample.Features.PatternTypeCode), Float(sample.Features.Rsi),
        Float(sample.Features.BollingerPosition), Float(sample.Features.VolumeRatio),
        Float(sample.Features.MarketRegimeCode), Float(sample.Features.AtrPercent),
        Float(sample.Features.HistoricalWinRate), Float(sample.Features.RiskRewardRatio),
        Float(sample.Features.PriceVsLongMovingAverage),
        Float(sample.Features.LongTrendHistoryAvailable), sample.IsWin);

    private static string Labels(IReadOnlyDictionary<uint, string>? labels) => labels is null
        ? string.Empty
        : string.Join(',', labels.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value}"));

    private static string Importances(IReadOnlyList<MlFeatureImportanceContract> values) =>
        string.Join(',', values.OrderBy(value => value.FeatureName, StringComparer.Ordinal)
            .Select(value => $"{value.FeatureName}:{Number(value.Importance)}"));

    private static string Float(float value) =>
        value.ToString("R", Invariant);

    private static string Number(double? value) => value.HasValue
        ? value.Value.ToString("R", Invariant)
        : string.Empty;
}

public static class MlTrainingContractPolicy
{
    public static string? CompatibilityError(MlTrainingJobRequest request)
    {
        if (request.RegimeEvidence is null || request.RegimeSamples is null
            || request.SignalSamples is null)
            return "missing-training-input";
        if (request.ContractVersion != MlTrainingContractVersions.Current)
            return "unsupported-contract-version";
        if (request.TrainerVersion != MlTrainingContractVersions.Trainer)
            return "unsupported-trainer-version";
        if (request.RegimeFeatureSchemaVersion != MlTrainingContractVersions.RegimeFeatureSchema
            || request.SignalFeatureSchemaVersion != MlTrainingContractVersions.SignalFeatureSchema)
            return "unsupported-feature-schema";
        if (request.RegimeClusterCount != MlTrainingContractVersions.RequiredRegimeClusters)
            return "invalid-regime-cluster-count";
        if (request.MinimumTrainingSamples < 1)
            return "invalid-minimum-samples";
        if (string.IsNullOrWhiteSpace(request.JobId) || request.JobId.Length > 128)
            return "invalid-job-id";
        if (request.RequestedAtUtc == default || request.ObservationCutoffUtc == default
            || MlTrainingContractHash.Utc(request.RequestedAtUtc)
                < MlTrainingContractHash.Utc(request.ObservationCutoffUtc))
            return "invalid-training-time";
        if (string.IsNullOrWhiteSpace(request.RegimeEvidence.Provider)
            || string.IsNullOrWhiteSpace(request.RegimeEvidence.Symbol)
            || string.IsNullOrWhiteSpace(request.RegimeEvidence.ContentHash)
            || string.IsNullOrWhiteSpace(request.RegimeEvidence.EvidenceId)
            || request.RegimeEvidence.FromUtc > request.RegimeEvidence.ToUtc
            || request.RegimeEvidence.ToUtc > request.ObservationCutoffUtc)
            return "invalid-regime-evidence";
        if (request.RegimeSamples.Select(sample => sample.AsOfUtc).Distinct().Count()
            != request.RegimeSamples.Count
            || request.SignalSamples.Select(sample => sample.SourceSignalId).Distinct().Count()
                != request.SignalSamples.Count)
            return "duplicate-training-sample";
        if (request.RegimeSamples.Any(sample => !Finite(
                sample.Return5Day, sample.Return10Day, sample.Return20Day,
                sample.VolatilityLevel, sample.VolumeChangeRate,
                sample.MaSlopePercent, sample.Rsi))
            || request.SignalSamples.Any(sample =>
                sample.Features.SchemaVersion != request.SignalFeatureSchemaVersion
                || !Finite(sample.Features.PatternTypeCode, sample.Features.Rsi,
                    sample.Features.BollingerPosition, sample.Features.VolumeRatio,
                    sample.Features.MarketRegimeCode, sample.Features.AtrPercent,
                    sample.Features.HistoricalWinRate, sample.Features.RiskRewardRatio,
                    sample.Features.PriceVsLongMovingAverage,
                    sample.Features.LongTrendHistoryAvailable)))
            return "invalid-training-feature";
        if (request.RegimeSamples.Any(sample =>
                MlTrainingContractHash.Utc(sample.AsOfUtc)
                > MlTrainingContractHash.Utc(request.ObservationCutoffUtc))
            || request.SignalSamples.Any(sample =>
                MlTrainingContractHash.Utc(sample.SignalBarAtUtc)
                > MlTrainingContractHash.Utc(request.ObservationCutoffUtc)))
            return "future-training-sample";
        if (!request.SignalSamples
                .OrderBy(sample => MlTrainingContractHash.Utc(sample.SignalBarAtUtc))
                .ThenBy(sample => sample.SourceSignalId)
                .SequenceEqual(request.SignalSamples))
            return "signal-samples-not-chronological";
        return string.Equals(
            MlTrainingContractHash.Input(request),
            request.InputHash,
            StringComparison.OrdinalIgnoreCase)
            ? null
            : "input-hash-mismatch";
    }

    public static string? ArtifactError(MlModelArtifactContract artifact)
    {
        if (artifact.ContractVersion != MlTrainingContractVersions.Current)
            return "unsupported-artifact-contract";
        if (artifact.TrainerVersion != MlTrainingContractVersions.Trainer)
            return "unsupported-artifact-trainer";
        var expected = artifact.ModelKind switch
        {
            MlModelKinds.MarketRegime => (
                MlTrainingContractVersions.RegimeFeatureSchema,
                MlTrainingContractVersions.RegimeFeatureCount),
            MlModelKinds.SignalScorer => (
                MlTrainingContractVersions.SignalFeatureSchema,
                MlTrainingContractVersions.SignalFeatureCount),
            _ => (-1, -1),
        };
        if (artifact.FeatureSchemaVersion != expected.Item1
            || artifact.FeatureCount != expected.Item2)
            return "artifact-feature-schema-mismatch";
        if (artifact.ModelBytes.Length == 0
            || !string.Equals(MlTrainingContractHash.Sha256(artifact.ModelBytes),
                artifact.ModelSha256, StringComparison.OrdinalIgnoreCase))
            return "artifact-model-hash-mismatch";
        if (artifact.ModelKind == MlModelKinds.MarketRegime
            && (artifact.ClusterLabels is null
                || artifact.ClusterLabels.Count != MlTrainingContractVersions.RequiredRegimeClusters
                || !artifact.ClusterLabels.Keys.ToHashSet().SetEquals([1u, 2u, 3u, 4u])
                || !artifact.ClusterLabels.Values.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(MlRegimeLabels.All)))
            return "incomplete-regime-labels";
        if (artifact.TrainingSamples < 1
            || artifact.TrainedAtUtc == default || artifact.TrainingCutoffUtc == default
            || artifact.TrainedAtUtc < artifact.TrainingCutoffUtc
            || artifact.FeatureImportances.Any(value =>
                string.IsNullOrWhiteSpace(value.FeatureName)
                || double.IsNaN(value.Importance) || double.IsInfinity(value.Importance)))
            return "invalid-artifact-metadata";
        return string.Equals(MlTrainingContractHash.Artifact(artifact), artifact.ArtifactId,
            StringComparison.OrdinalIgnoreCase)
            ? null
            : "artifact-id-mismatch";
    }

    private static bool Finite(params float[] values) =>
        values.All(value => !float.IsNaN(value) && !float.IsInfinity(value));
}
