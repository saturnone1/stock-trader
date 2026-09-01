using FluentAssertions;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Tests;

public sealed class TradingCoreStage5EvidenceTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Complete_ordered_evidence_derives_acceptance()
    {
        var isolated = Isolated();
        var candidate = Candidate();
        var local = Local(candidate.CandidateId);
        var manifests = Chain(isolated.ManifestId);

        var index = Stage5EvidencePolicy.DeriveIndex(
            candidate, local, isolated, manifests, "review-1", Now);

        Stage5EvidencePolicy.IndexError(index).Should().BeNull();
        index.Accepted.Should().BeTrue();
        index.ActiveStopReasons.Should().BeEmpty();
        index.AuthorityGenerations.Should().Equal(1, 2, 2, 3, 4);
    }

    [Fact]
    public void Evidence_from_an_unlinked_manifest_cannot_be_combined()
    {
        var isolated = Isolated();
        var candidate = Candidate();
        var local = Local(candidate.CandidateId);
        var manifests = Chain(isolated.ManifestId).ToArray();
        manifests[2] = manifests[2] with { PreviousManifestId = "unlinked" };
        manifests[2] = manifests[2] with
        {
            ManifestId = Stage5EvidenceIdentity.Operational(manifests[2])
        };

        var index = Stage5EvidencePolicy.DeriveIndex(
            candidate, local, isolated, manifests, "review-1", Now);

        index.Accepted.Should().BeFalse();
        index.ActiveStopReasons.Should().Contain(
            "manifest-chain-invalid:RemoteRecovery");
    }

    [Fact]
    public void Evidence_from_a_different_binary_set_cannot_be_combined()
    {
        var isolated = Isolated();
        var candidate = Candidate();
        var local = Local(candidate.CandidateId);
        var manifests = Chain(isolated.ManifestId).ToArray();
        var changed = manifests[0].ImageDigests.ToDictionary(pair => pair.Key,
            pair => pair.Key == "edge" ? new string('b', 64) : pair.Value);
        manifests[0] = manifests[0] with { ImageDigests = changed, ManifestId = "" };
        manifests[0] = manifests[0] with
        {
            ManifestId = Stage5EvidenceIdentity.Operational(manifests[0])
        };

        var index = Stage5EvidencePolicy.DeriveIndex(
            candidate, local, isolated, manifests, "review-1", Now);

        index.Accepted.Should().BeFalse();
        index.ActiveStopReasons.Should().Contain("candidate-binary-set-mismatch");
    }

    private static AcceptanceManifestV1 Isolated()
    {
        var scenarios = TradingCoreAcceptanceScenarioCatalog.Required.Select(code =>
            new AcceptanceScenarioResult(Guid.NewGuid().ToString(), code, Hash, Hash, Hash,
                [Hash], Now, Now.AddSeconds(1), true, null)).ToArray();
        var candidate = new AcceptanceManifestV1(
            TradingCoreAcceptanceVersions.Current, "", "run-1", "IsolatedAcceptance",
            "commit", "build", CandidateImages(), SharedAssemblies(), scenarios,
            Now, Now.AddMinutes(1), true, []);
        return candidate with
        {
            ManifestId = TradingCoreAcceptanceIdentity.Manifest(candidate)
        };
    }

    private static TradingCoreCandidateManifestV1 Candidate()
    {
        var input = new Stage5CandidateManifestInput(
            TradingCoreAcceptanceVersions.Current, "commit", Hash, Hash, "build",
            CandidateImages(), Map("base"), SharedAssemblies(), Map("inventory"),
            Map("sbom"), Map("package"), Map("migration"), Hash, Map("k8s"),
            Map("catalog"), ["scope"], ["backup"], Now);
        var value = Stage5EvidencePolicy.SealCandidate(input);
        Stage5EvidencePolicy.CandidateError(value).Should().BeNull();
        return value;
    }

    private static LocalVerificationManifestV1 Local(string candidateId)
    {
        var commands = Stage5LocalGateCatalog.RequiredCommands.Select(command =>
            new Stage5LocalCommandResult(command, 0, null, Hash, 1)).ToArray();
        var input = new Stage5LocalVerificationInput(
            TradingCoreAcceptanceVersions.Current, candidateId, commands,
            Map("openapi"), Now, Now.AddMinutes(1), []);
        var value = Stage5EvidencePolicy.SealLocal(input);
        Stage5EvidencePolicy.LocalError(value).Should().BeNull();
        return value;
    }

    private static IReadOnlyList<Stage5OperationalManifestV1> Chain(string previous)
    {
        var definitions = new[]
        {
            (Stage5EnvironmentClasses.ProductionShadow, 1L, 1L,
                AuthorityOwners.Edge, TradingAuthorityMode.Shadow,
                AuthorityOwners.Edge, TradingAuthorityMode.Shadow),
            (Stage5EnvironmentClasses.ProductionCutover, 1L, 2L,
                AuthorityOwners.Edge, TradingAuthorityMode.Shadow,
                AuthorityOwners.TradingCore, TradingAuthorityMode.Remote),
            (Stage5EnvironmentClasses.RemoteRecovery, 2L, 2L,
                AuthorityOwners.TradingCore, TradingAuthorityMode.Remote,
                AuthorityOwners.TradingCore, TradingAuthorityMode.Remote),
            (Stage5EnvironmentClasses.ProductionRollback, 2L, 3L,
                AuthorityOwners.TradingCore, TradingAuthorityMode.Remote,
                AuthorityOwners.Edge, TradingAuthorityMode.Shadow),
            (Stage5EnvironmentClasses.FinalRemote, 3L, 4L,
                AuthorityOwners.Edge, TradingAuthorityMode.Shadow,
                AuthorityOwners.TradingCore, TradingAuthorityMode.Remote),
        };
        var values = new List<Stage5OperationalManifestV1>();
        foreach (var (environment, initialGeneration, finalGeneration,
                     initialOwner, initialMode, finalOwner, finalMode) in definitions)
        {
            var input = new Stage5OperationalEvidenceInput(
                TradingCoreAcceptanceVersions.Current, Candidate().CandidateId, environment, previous,
                "commit", "build", CandidateImages(), SharedAssemblies(),
                Authority(initialGeneration, initialOwner, initialMode),
                Authority(finalGeneration, finalOwner, finalMode), [Hash], Map("backup"),
                Now, Now.AddMinutes(1), []);
            var manifest = Stage5EvidencePolicy.Seal(input);
            Stage5EvidencePolicy.Error(manifest).Should().BeNull();
            values.Add(manifest);
            previous = manifest.ManifestId;
        }
        return values;
    }

    private static Stage5AuthorityEvidence Authority(
        long generation, string owner, TradingAuthorityMode mode) => new(
        mode.ToString(), owner, generation, 1, AuthorityCommandAcceptanceStates.Open,
        0, 0, 0, 0, 0, Hash, Hash, Hash);

    private static IReadOnlyDictionary<string, string> Map(string key) =>
        new Dictionary<string, string> { [key] = Hash };

    private static IReadOnlyDictionary<string, string> CandidateImages() =>
        TradingCoreAcceptanceImageCatalog.Required.ToDictionary(key => key, _ => Hash);

    private static IReadOnlyDictionary<string, string> SharedAssemblies() =>
        TradingCoreAcceptanceAssemblyCatalog.Required.ToDictionary(key => key, _ => Hash);
}
