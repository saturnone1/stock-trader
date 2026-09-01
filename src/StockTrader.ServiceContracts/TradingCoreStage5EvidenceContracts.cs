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

public static class Stage5LocalGateCatalog
{
    public static IReadOnlyList<string> RequiredCommands { get; } =
    [
        "dotnet-build",
        "dotnet-test",
        "desktop-api-check",
        "desktop-test",
        "desktop-build"
    ];
}

public sealed record Stage5CandidateManifestInput(
    int ContractVersion,
    string RepositoryCommit,
    string WorktreeInputHash,
    string DependencyLockHash,
    string BuildId,
    IReadOnlyDictionary<string, string> ImageDigests,
    IReadOnlyDictionary<string, string> BaseImageDigests,
    IReadOnlyDictionary<string, string> SharedAssemblyHashes,
    IReadOnlyDictionary<string, string> AssemblyInventoryHashes,
    IReadOnlyDictionary<string, string> SbomHashes,
    IReadOnlyDictionary<string, string> PackageGraphHashes,
    IReadOnlyDictionary<string, string> MigrationHashes,
    string OpenApiContractHash,
    IReadOnlyDictionary<string, string> KubernetesObjectHashes,
    IReadOnlyDictionary<string, string> CatalogHashes,
    IReadOnlyList<string> DeploymentScopes,
    IReadOnlyList<string> RollbackRequirements,
    DateTime CreatedAtUtc);

public sealed record TradingCoreCandidateManifestV1(
    int ContractVersion,
    string CandidateId,
    string RepositoryCommit,
    string WorktreeInputHash,
    string DependencyLockHash,
    string BuildId,
    IReadOnlyDictionary<string, string> ImageDigests,
    IReadOnlyDictionary<string, string> BaseImageDigests,
    IReadOnlyDictionary<string, string> SharedAssemblyHashes,
    IReadOnlyDictionary<string, string> AssemblyInventoryHashes,
    IReadOnlyDictionary<string, string> SbomHashes,
    IReadOnlyDictionary<string, string> PackageGraphHashes,
    IReadOnlyDictionary<string, string> MigrationHashes,
    string OpenApiContractHash,
    IReadOnlyDictionary<string, string> KubernetesObjectHashes,
    IReadOnlyDictionary<string, string> CatalogHashes,
    IReadOnlyList<string> DeploymentScopes,
    IReadOnlyList<string> RollbackRequirements,
    DateTime CreatedAtUtc);

public sealed record Stage5LocalCommandResult(
    string CommandId,
    int ExitCode,
    int? TestTotal,
    string OutputHash,
    long DurationMilliseconds);

public sealed record Stage5LocalVerificationInput(
    int ContractVersion,
    string CandidateId,
    IReadOnlyList<Stage5LocalCommandResult> Commands,
    IReadOnlyDictionary<string, string> GeneratedContractHashes,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    IReadOnlyList<string> StopReasons);

public sealed record LocalVerificationManifestV1(
    int ContractVersion,
    string ManifestId,
    string CandidateId,
    IReadOnlyList<Stage5LocalCommandResult> Commands,
    IReadOnlyDictionary<string, string> GeneratedContractHashes,
    DateTime StartedAtUtc,
    DateTime EndedAtUtc,
    bool Passed,
    IReadOnlyList<string> StopReasons);

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
    int ShadowObservationCount,
    int ShadowMismatchCount,
    DateTime? LastBrokerReconciledAtUtc,
    bool DatabaseIntegrityPassed,
    bool ResourceObjectivesPassed,
    string? HealthError,
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
    string CandidateManifestId,
    string LocalVerificationManifestId,
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
    public static string Candidate(TradingCoreCandidateManifestV1 manifest) =>
        CanonicalJsonHash.Compute(manifest, nameof(TradingCoreCandidateManifestV1.CandidateId));

    public static string Local(LocalVerificationManifestV1 manifest) =>
        CanonicalJsonHash.Compute(manifest, nameof(LocalVerificationManifestV1.ManifestId));

    public static string Operational(Stage5OperationalManifestV1 manifest) =>
        CanonicalJsonHash.Compute(manifest, nameof(Stage5OperationalManifestV1.ManifestId));

    public static string Index(Stage5AcceptanceIndexV1 index) =>
        CanonicalJsonHash.Compute(index, nameof(Stage5AcceptanceIndexV1.IndexId));
}

public static class Stage5EvidencePolicy
{
    public static TradingCoreCandidateManifestV1 SealCandidate(Stage5CandidateManifestInput input)
    {
        var candidate = new TradingCoreCandidateManifestV1(
            input.ContractVersion, "", input.RepositoryCommit, input.WorktreeInputHash,
            input.DependencyLockHash, input.BuildId, input.ImageDigests,
            input.BaseImageDigests, input.SharedAssemblyHashes, input.AssemblyInventoryHashes,
            input.SbomHashes, input.PackageGraphHashes, input.MigrationHashes,
            input.OpenApiContractHash, input.KubernetesObjectHashes, input.CatalogHashes,
            input.DeploymentScopes, input.RollbackRequirements, input.CreatedAtUtc);
        return candidate with { CandidateId = Stage5EvidenceIdentity.Candidate(candidate) };
    }

    public static string? CandidateError(TradingCoreCandidateManifestV1 manifest)
    {
        if (manifest is null || manifest.ContractVersion != TradingCoreAcceptanceVersions.Current
            || manifest.CandidateId != Stage5EvidenceIdentity.Candidate(manifest)
            || string.IsNullOrWhiteSpace(manifest.RepositoryCommit)
            || string.IsNullOrWhiteSpace(manifest.BuildId)
            || manifest.CreatedAtUtc.Kind != DateTimeKind.Utc
            || NotHash(manifest.WorktreeInputHash) || NotHash(manifest.DependencyLockHash)
            || NotHash(manifest.OpenApiContractHash)
            || !HasHashes(manifest.ImageDigests, TradingCoreAcceptanceImageCatalog.Required)
            || !HasHashes(manifest.SharedAssemblyHashes,
                TradingCoreAcceptanceAssemblyCatalog.Required)
            || !HasHashSet(manifest.BaseImageDigests)
            || !HasHashSet(manifest.AssemblyInventoryHashes)
            || !HasHashSet(manifest.SbomHashes)
            || !HasHashSet(manifest.PackageGraphHashes)
            || !HasHashSet(manifest.MigrationHashes)
            || !HasHashSet(manifest.KubernetesObjectHashes)
            || !HasHashSet(manifest.CatalogHashes)
            || manifest.DeploymentScopes.Count == 0
            || manifest.RollbackRequirements.Count == 0)
            return "stage5-candidate-manifest-invalid";
        return null;
    }

    public static LocalVerificationManifestV1 SealLocal(Stage5LocalVerificationInput input)
    {
        var reasons = LocalStopReasons(input).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var candidate = new LocalVerificationManifestV1(
            input.ContractVersion, "", input.CandidateId, input.Commands,
            input.GeneratedContractHashes, input.StartedAtUtc, input.EndedAtUtc,
            reasons.Length == 0, reasons);
        return candidate with { ManifestId = Stage5EvidenceIdentity.Local(candidate) };
    }

    public static string? LocalError(LocalVerificationManifestV1 manifest)
    {
        if (manifest is null || manifest.ContractVersion != TradingCoreAcceptanceVersions.Current
            || manifest.ManifestId != Stage5EvidenceIdentity.Local(manifest))
            return "stage5-local-verification-invalid";
        var input = new Stage5LocalVerificationInput(
            manifest.ContractVersion, manifest.CandidateId, manifest.Commands,
            manifest.GeneratedContractHashes, manifest.StartedAtUtc, manifest.EndedAtUtc,
            manifest.StopReasons);
        var reasons = LocalStopReasons(input).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        return manifest.Passed == (reasons.Length == 0)
            && manifest.StopReasons.SequenceEqual(reasons, StringComparer.Ordinal)
            ? null : "stage5-local-verdict-invalid";
    }

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
        TradingCoreCandidateManifestV1 candidate,
        LocalVerificationManifestV1 local,
        AcceptanceManifestV1 isolated,
        IReadOnlyList<Stage5OperationalManifestV1> operational,
        string reviewIdentity,
        DateTime reviewedAtUtc)
    {
        var stops = new List<string>();
        var candidateId = candidate.CandidateId;
        if (CandidateError(candidate) is not null)
            stops.Add("candidate-manifest-invalid");
        if (LocalError(local) is not null || !local.Passed
            || local.CandidateId != candidateId)
            stops.Add("local-verification-invalid");
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
        if (!SameMap(isolated.ImageDigests, candidate.ImageDigests)
            || !SameMap(isolated.SharedAssemblyHashes, candidate.SharedAssemblyHashes)
            || operational.Any(manifest =>
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
        var indexCandidate = new Stage5AcceptanceIndexV1(
            TradingCoreAcceptanceVersions.Current, "", candidateId, candidate.CandidateId,
            local.ManifestId, isolated.ManifestId,
            operational.Select(value => value.ManifestId).ToArray(),
            operational.Select(value => value.FinalAuthority.Generation).ToArray(),
            final?.Owner ?? "", final?.Mode ?? "", finalUnresolved, reasons,
            reviewIdentity, reviewedAtUtc, accepted);
        return indexCandidate with { IndexId = Stage5EvidenceIdentity.Index(indexCandidate) };
    }

    public static string? IndexError(Stage5AcceptanceIndexV1 index) =>
        index is null || index.ContractVersion != TradingCoreAcceptanceVersions.Current
        || index.IndexId != Stage5EvidenceIdentity.Index(index)
        || string.IsNullOrWhiteSpace(index.CandidateManifestId)
        || string.IsNullOrWhiteSpace(index.LocalVerificationManifestId)
        || index.Accepted != (index.ActiveStopReasons.Count == 0
            && index.OperationalManifestIds.Count == Stage5EnvironmentClasses.OperationalOrder.Count
            && index.FinalAuthorityOwner == AuthorityOwners.TradingCore
            && index.FinalAuthorityMode == TradingAuthorityMode.Remote.ToString()
            && index.FinalUnresolvedCount == 0)
            ? "stage5-acceptance-index-invalid" : null;

    private static IEnumerable<string> LocalStopReasons(Stage5LocalVerificationInput input)
    {
        foreach (var reason in input.StopReasons.Where(value => !string.IsNullOrWhiteSpace(value)))
            yield return reason;
        if (input.ContractVersion != TradingCoreAcceptanceVersions.Current
            || string.IsNullOrWhiteSpace(input.CandidateId)
            || input.StartedAtUtc.Kind != DateTimeKind.Utc
            || input.EndedAtUtc.Kind != DateTimeKind.Utc
            || input.EndedAtUtc < input.StartedAtUtc)
            yield return "local-verification-identity-invalid";
        var commandIds = input.Commands.Select(value => value.CommandId).ToArray();
        if (!commandIds.SequenceEqual(Stage5LocalGateCatalog.RequiredCommands,
                StringComparer.Ordinal)
            || input.Commands.Any(value => value.ExitCode != 0
                || value.DurationMilliseconds < 0 || NotHash(value.OutputHash)))
            yield return "local-verification-command-failed";
        if (!HasHashSet(input.GeneratedContractHashes))
            yield return "local-verification-contract-hash-invalid";
    }

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
        if (input.EnvironmentClass == Stage5EnvironmentClasses.ProductionShadow
            && (input.FinalAuthority.ShadowObservationCount < 2
                || input.FinalAuthority.ShadowMismatchCount != 0))
            yield return "production-shadow-corpus-incomplete";
        if (input.EnvironmentClass != Stage5EnvironmentClasses.ProductionShadow
            && input.FinalAuthority.LastBrokerReconciledAtUtc is null)
            yield return "broker-reconciliation-evidence-missing";
        if (Unresolved(input.FinalAuthority) != 0)
            yield return "financial-state-not-converged";
    }

    private static string? AuthorityError(Stage5AuthorityEvidence authority) =>
        authority is null || authority.Generation < 1 || authority.AccountGeneration < 1
        || string.IsNullOrWhiteSpace(authority.Mode) || string.IsNullOrWhiteSpace(authority.Owner)
        || string.IsNullOrWhiteSpace(authority.CommandAcceptance)
        || authority.UnresolvedIntentCount < 0 || authority.UnresolvedBrokerEffectCount < 0
        || authority.UnprocessedBrokerFillCount < 0 || authority.DivergenceCount < 0
        || authority.EnabledConsumerLag < 0 || authority.ShadowObservationCount < 0
        || authority.ShadowMismatchCount < 0
        || (authority.LastBrokerReconciledAtUtc is { Kind: not DateTimeKind.Utc })
        || !authority.DatabaseIntegrityPassed || !authority.ResourceObjectivesPassed
        || !string.IsNullOrWhiteSpace(authority.HealthError)
        || NotHash(authority.CapabilityReceiptHash)
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

    private static bool HasHashes(IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> required) =>
        required.All(values.ContainsKey) && values.Values.All(value => !NotHash(value));

    private static bool HasHashSet(IReadOnlyDictionary<string, string> values) =>
        values.Count > 0 && values.Values.All(value => !NotHash(value));
}
