namespace StockTrader.ServiceContracts.TradingCore;

public static class Stage5EnvironmentClasses
{
    public const string IsolatedAcceptance = "IsolatedAcceptance";
    public const string ProductionShadow = "ProductionShadow";
    public const string ProductionCutover = "ProductionCutover";
    public const string RemoteRecovery = "RemoteRecovery";
    public const string ProductionRollback = "ProductionRollback";
    public const string FinalRemote = "FinalRemote";

    public static IReadOnlyList<string> OperationalOrder { get; } =
    [ProductionShadow, ProductionCutover, RemoteRecovery, ProductionRollback, FinalRemote];
}

public sealed record Stage5AuthorityEvidence(
    string Mode,
    string Owner,
    long Generation,
    long AccountGeneration,
    string CommandAcceptance,
    int UnresolvedIntentCount,
    int UnresolvedBrokerEffectCount,
    int UnprocessedBrokerFillCount,
    int DivergenceCount,
    long EnabledConsumerLag,
    string CapabilityReceiptHash,
    string ReconciliationHash,
    string DatabaseIntegrityHash);

public sealed record Stage5OperationalEvidenceInput(
    int ContractVersion,
    string CandidateId,
    string EnvironmentClass,
    string PreviousManifestId,
    string RepositoryCommit,
    string BuildId,
    IReadOnlyDictionary<string, string> ImageDigests,
    IReadOnlyDictionary<string, string> SharedAssemblyHashes,
    Stage5AuthorityEvidence InitialAuthority,
    Stage5AuthorityEvidence FinalAuthority,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyDictionary<string, string> RecoveryArtifactHashes,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    IReadOnlyList<string> StopReasons);

public sealed record Stage5OperationalManifestV1(
    int ContractVersion,
    string ManifestId,
    string CandidateId,
    string EnvironmentClass,
    string PreviousManifestId,
    string RepositoryCommit,
    string BuildId,
    IReadOnlyDictionary<string, string> ImageDigests,
    IReadOnlyDictionary<string, string> SharedAssemblyHashes,
    Stage5AuthorityEvidence InitialAuthority,
    Stage5AuthorityEvidence FinalAuthority,
    IReadOnlyList<string> EvidenceReferences,
    IReadOnlyDictionary<string, string> RecoveryArtifactHashes,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    bool Passed,
    IReadOnlyList<string> StopReasons);

public sealed record Stage5AcceptanceIndexV1(
    int ContractVersion,
    string IndexId,
    string CandidateId,
    string IsolatedManifestId,
    IReadOnlyList<string> OperationalManifestIds,
    IReadOnlyList<long> AuthorityGenerations,
    string FinalAuthorityOwner,
    string FinalAuthorityMode,
    int FinalUnresolvedCount,
    IReadOnlyList<string> ActiveStopReasons,
    string ReviewIdentity,
    DateTime ReviewedAtUtc,
    bool Accepted);

public static class Stage5EvidenceIdentity
{
    public static string Operational(Stage5OperationalManifestV1 manifest) =>
        CanonicalJsonHash.Compute(manifest, nameof(Stage5OperationalManifestV1.ManifestId));

    public static string Index(Stage5AcceptanceIndexV1 index) =>
        CanonicalJsonHash.Compute(index, nameof(Stage5AcceptanceIndexV1.IndexId));
}

public static class Stage5EvidencePolicy
{
    public static Stage5OperationalManifestV1 Seal(Stage5OperationalEvidenceInput input)
    {
        var reasons = InputStopReasons(input).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var passed = reasons.Length == 0;
        var candidate = new Stage5OperationalManifestV1(
            input.ContractVersion, "", input.CandidateId, input.EnvironmentClass,
            input.PreviousManifestId, input.RepositoryCommit, input.BuildId,
            input.ImageDigests, input.SharedAssemblyHashes, input.InitialAuthority,
            input.FinalAuthority, input.EvidenceReferences, input.RecoveryArtifactHashes,
            input.StartedAtUtc, input.EndedAtUtc, passed, reasons);
        return candidate with { ManifestId = Stage5EvidenceIdentity.Operational(candidate) };
    }

    public static string? Error(Stage5OperationalManifestV1 manifest)
    {
        if (manifest is null || manifest.ContractVersion != TradingCoreAcceptanceVersions.Current
            || !Stage5EnvironmentClasses.OperationalOrder.Contains(manifest.EnvironmentClass,
                StringComparer.Ordinal)
            || manifest.ManifestId != Stage5EvidenceIdentity.Operational(manifest))
            return "stage5-operational-manifest-invalid";
        var input = new Stage5OperationalEvidenceInput(
            manifest.ContractVersion, manifest.CandidateId, manifest.EnvironmentClass,
            manifest.PreviousManifestId, manifest.RepositoryCommit, manifest.BuildId,
            manifest.ImageDigests, manifest.SharedAssemblyHashes, manifest.InitialAuthority,
            manifest.FinalAuthority, manifest.EvidenceReferences,
            manifest.RecoveryArtifactHashes, manifest.StartedAtUtc, manifest.EndedAtUtc,
            manifest.StopReasons);
        var reasons = InputStopReasons(input).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return manifest.Passed == (reasons.Length == 0)
            && manifest.StopReasons.SequenceEqual(reasons, StringComparer.Ordinal)
            ? null : "stage5-operational-verdict-invalid";
    }

    public static Stage5AcceptanceIndexV1 DeriveIndex(
        string candidateId,
        AcceptanceManifestV1 isolated,
        IReadOnlyList<Stage5OperationalManifestV1> operational,
        string reviewIdentity,
        DateTime reviewedAtUtc)
    {
        var stops = new List<string>();
        var isolatedError = TradingCoreAcceptancePolicy.ManifestError(isolated);
        if (isolatedError is not null || !isolated.Passed)
            stops.Add(isolatedError ?? "isolated-acceptance-failed");
        if (operational.Count != Stage5EnvironmentClasses.OperationalOrder.Count)
            stops.Add("operational-manifest-coverage-invalid");
        for (var index = 0; index < Math.Min(operational.Count,
                 Stage5EnvironmentClasses.OperationalOrder.Count); index++)
        {
            var manifest = operational[index];
            if (manifest.EnvironmentClass != Stage5EnvironmentClasses.OperationalOrder[index]
                || Error(manifest) is not null || !manifest.Passed
                || manifest.CandidateId != candidateId)
                stops.Add($"operational-manifest-invalid:{Stage5EnvironmentClasses.OperationalOrder[index]}");
            var expectedPrevious = index == 0
                ? isolated.ManifestId
                : operational[index - 1].ManifestId;
            if (manifest.PreviousManifestId != expectedPrevious)
                stops.Add($"manifest-chain-invalid:{manifest.EnvironmentClass}");
        }
        if (operational.Count == Stage5EnvironmentClasses.OperationalOrder.Count)
            ValidateAuthorityChain(operational, stops);
        if (operational.Any(manifest =>
                !SameMap(manifest.ImageDigests, isolated.ImageDigests)
                || !SameMap(manifest.SharedAssemblyHashes, isolated.SharedAssemblyHashes)))
            stops.Add("candidate-binary-set-mismatch");
        if (string.IsNullOrWhiteSpace(candidateId) || string.IsNullOrWhiteSpace(reviewIdentity)
            || reviewedAtUtc.Kind != DateTimeKind.Utc)
            stops.Add("acceptance-review-identity-invalid");
        var final = operational.LastOrDefault()?.FinalAuthority;
        var finalUnresolved = final is null ? int.MaxValue : Unresolved(final);
        var reasons = stops.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var accepted = reasons.Length == 0 && final is not null
            && final.Owner == AuthorityOwners.TradingCore
            && final.Mode == TradingAuthorityMode.Remote.ToString()
            && final.CommandAcceptance == AuthorityCommandAcceptanceStates.Open
            && finalUnresolved == 0;
        if (!accepted && reasons.Length == 0) reasons = ["final-remote-authority-invalid"];
        var candidate = new Stage5AcceptanceIndexV1(
            TradingCoreAcceptanceVersions.Current, "", candidateId, isolated.ManifestId,
            operational.Select(value => value.ManifestId).ToArray(),
            operational.Select(value => value.FinalAuthority.Generation).ToArray(),
            final?.Owner ?? "", final?.Mode ?? "", finalUnresolved, reasons,
            reviewIdentity, reviewedAtUtc, accepted);
        return candidate with { IndexId = Stage5EvidenceIdentity.Index(candidate) };
    }

    public static string? IndexError(Stage5AcceptanceIndexV1 index) =>
        index is null || index.ContractVersion != TradingCoreAcceptanceVersions.Current
        || index.IndexId != Stage5EvidenceIdentity.Index(index)
        || index.Accepted != (index.ActiveStopReasons.Count == 0
            && index.OperationalManifestIds.Count == Stage5EnvironmentClasses.OperationalOrder.Count
            && index.FinalAuthorityOwner == AuthorityOwners.TradingCore
            && index.FinalAuthorityMode == TradingAuthorityMode.Remote.ToString()
            && index.FinalUnresolvedCount == 0)
            ? "stage5-acceptance-index-invalid" : null;

    private static IEnumerable<string> InputStopReasons(Stage5OperationalEvidenceInput input)
    {
        foreach (var reason in input.StopReasons.Where(value => !string.IsNullOrWhiteSpace(value)))
            yield return reason;
        if (input.ContractVersion != TradingCoreAcceptanceVersions.Current
            || !Stage5EnvironmentClasses.OperationalOrder.Contains(input.EnvironmentClass,
                StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(input.CandidateId)
            || string.IsNullOrWhiteSpace(input.PreviousManifestId)
            || string.IsNullOrWhiteSpace(input.RepositoryCommit)
            || string.IsNullOrWhiteSpace(input.BuildId)
            || input.StartedAtUtc.Kind != DateTimeKind.Utc
            || input.EndedAtUtc.Kind != DateTimeKind.Utc
            || input.EndedAtUtc < input.StartedAtUtc)
            yield return "operational-identity-invalid";
        if (input.ImageDigests.Count == 0 || input.ImageDigests.Values.Any(NotHash)
            || input.SharedAssemblyHashes.Count == 0
            || input.SharedAssemblyHashes.Values.Any(NotHash))
            yield return "candidate-hash-set-invalid";
        if (input.EvidenceReferences.Count == 0
            || input.RecoveryArtifactHashes.Count == 0
            || input.RecoveryArtifactHashes.Values.Any(NotHash))
            yield return "operational-evidence-incomplete";
        if (AuthorityError(input.InitialAuthority) is not null
            || AuthorityError(input.FinalAuthority) is not null)
            yield return "authority-evidence-invalid";
        if (Unresolved(input.FinalAuthority) != 0)
            yield return "financial-state-not-converged";
    }

    private static string? AuthorityError(Stage5AuthorityEvidence authority) =>
        authority is null || authority.Generation < 1 || authority.AccountGeneration < 1
        || string.IsNullOrWhiteSpace(authority.Mode) || string.IsNullOrWhiteSpace(authority.Owner)
        || string.IsNullOrWhiteSpace(authority.CommandAcceptance)
        || authority.UnresolvedIntentCount < 0 || authority.UnresolvedBrokerEffectCount < 0
        || authority.UnprocessedBrokerFillCount < 0 || authority.DivergenceCount < 0
        || authority.EnabledConsumerLag < 0 || NotHash(authority.CapabilityReceiptHash)
        || NotHash(authority.ReconciliationHash) || NotHash(authority.DatabaseIntegrityHash)
            ? "authority-evidence-invalid" : null;

    private static void ValidateAuthorityChain(
        IReadOnlyList<Stage5OperationalManifestV1> values, ICollection<string> stops)
    {
        for (var index = 1; index < values.Count; index++)
            if (values[index].InitialAuthority.Generation
                != values[index - 1].FinalAuthority.Generation)
                stops.Add($"authority-chain-invalid:{values[index].EnvironmentClass}");
        var shadow = values[0];
        var cutover = values[1];
        var recovery = values[2];
        var rollback = values[3];
        var final = values[4];
        if (shadow.FinalAuthority.Generation != shadow.InitialAuthority.Generation
            || cutover.FinalAuthority.Generation != cutover.InitialAuthority.Generation + 1
            || recovery.FinalAuthority.Generation != recovery.InitialAuthority.Generation
            || rollback.FinalAuthority.Generation != rollback.InitialAuthority.Generation + 1
            || final.FinalAuthority.Generation != final.InitialAuthority.Generation + 1)
            stops.Add("authority-generation-sequence-invalid");
        if (!IsAuthority(shadow.InitialAuthority, AuthorityOwners.Edge, TradingAuthorityMode.Shadow)
            || !IsAuthority(shadow.FinalAuthority, AuthorityOwners.Edge, TradingAuthorityMode.Shadow)
            || !IsAuthority(cutover.InitialAuthority, AuthorityOwners.Edge, TradingAuthorityMode.Shadow)
            || !IsAuthority(cutover.FinalAuthority, AuthorityOwners.TradingCore, TradingAuthorityMode.Remote)
            || !IsAuthority(recovery.InitialAuthority, AuthorityOwners.TradingCore, TradingAuthorityMode.Remote)
            || !IsAuthority(recovery.FinalAuthority, AuthorityOwners.TradingCore, TradingAuthorityMode.Remote)
            || !IsAuthority(rollback.InitialAuthority, AuthorityOwners.TradingCore, TradingAuthorityMode.Remote)
            || !IsAuthority(rollback.FinalAuthority, AuthorityOwners.Edge, TradingAuthorityMode.Shadow)
            || !IsAuthority(final.InitialAuthority, AuthorityOwners.Edge, TradingAuthorityMode.Shadow)
            || !IsAuthority(final.FinalAuthority, AuthorityOwners.TradingCore, TradingAuthorityMode.Remote)
            || cutover.FinalAuthority.Owner != AuthorityOwners.TradingCore
            || cutover.FinalAuthority.Mode != TradingAuthorityMode.Remote.ToString()
            || rollback.FinalAuthority.Owner != AuthorityOwners.Edge
            || rollback.FinalAuthority.Mode != TradingAuthorityMode.Shadow.ToString()
            || final.FinalAuthority.Owner != AuthorityOwners.TradingCore
            || final.FinalAuthority.Mode != TradingAuthorityMode.Remote.ToString())
            stops.Add("authority-owner-sequence-invalid");
    }

    private static bool IsAuthority(
        Stage5AuthorityEvidence value, string owner, TradingAuthorityMode mode) =>
        value.Owner == owner && value.Mode == mode.ToString();

    private static int Unresolved(Stage5AuthorityEvidence value) => checked(
        value.UnresolvedIntentCount + value.UnresolvedBrokerEffectCount
        + value.UnprocessedBrokerFillCount + value.DivergenceCount
        + (int)Math.Min(value.EnabledConsumerLag, int.MaxValue));

    private static bool NotHash(string value)
    {
        if (value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            value = value[7..];
        return string.IsNullOrWhiteSpace(value) || value.Length < 32
            || value.Any(character => !Uri.IsHexDigit(character));
    }

    private static bool SameMap(IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase));
}
