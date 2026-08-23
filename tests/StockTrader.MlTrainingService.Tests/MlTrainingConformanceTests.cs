using System.Text.Json;
using FluentAssertions;
using StockTrader.MlTrainingCompute;
using StockTrader.MlTrainingService;
using StockTrader.ServiceContracts.MachineLearning;

namespace StockTrader.MlTrainingService.Tests;

public sealed class MlTrainingConformanceTests
{
    [Fact]
    public void Compute_is_deterministic_and_keeps_insufficient_signal_explicit()
    {
        var request = Request();
        var first = MlTrainingComputeFacade.Train(request);
        var second = MlTrainingComputeFacade.Train(request);

        first.Status.Should().Be(MlTrainingJobStatuses.PartiallyCompleted);
        first.SignalArtifact.Should().BeNull();
        first.RegimeArtifact.Should().NotBeNull();
        first.RegimeArtifact!.ClusterLabels.Should().BeEquivalentTo(second.RegimeArtifact!.ClusterLabels);
        MlTrainingContractPolicy.ArtifactError(first.RegimeArtifact).Should().BeNull();
        MlTrainingComputeFacade.PredictRegime(
            first.RegimeArtifact.ModelBytes, request.RegimeSamples[^1])
            .Should().Be(MlTrainingComputeFacade.PredictRegime(
                second.RegimeArtifact.ModelBytes, request.RegimeSamples[^1]));
    }

    [Fact]
    public void Contract_rejects_future_mutated_and_incomplete_artifacts()
    {
        var request = Request();
        var future = request with
        {
            RegimeSamples = request.RegimeSamples.Append(
                request.RegimeSamples[^1] with
                { AsOfUtc = request.ObservationCutoffUtc.AddMinutes(1) }).ToArray()
        };
        future = future with { InputHash = MlTrainingContractHash.Input(future) };
        MlTrainingContractPolicy.CompatibilityError(future).Should().Be("future-training-sample");

        var mutated = request with { MinimumTrainingSamples = request.MinimumTrainingSamples + 1 };
        MlTrainingContractPolicy.CompatibilityError(mutated).Should().Be("input-hash-mismatch");

        var artifact = MlTrainingComputeFacade.Train(request).RegimeArtifact!;
        var incomplete = artifact with
        {
            ClusterLabels = artifact.ClusterLabels!.Where(x => x.Key != 4)
                .ToDictionary(x => x.Key, x => x.Value)
        };
        incomplete = incomplete with { ArtifactId = MlTrainingContractHash.Artifact(incomplete) };
        MlTrainingContractPolicy.ArtifactError(incomplete).Should().Be("incomplete-regime-labels");
    }

    [Fact]
    public void Durable_store_is_idempotent_and_rejects_identity_reuse()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ml-training-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new JobStore(Path.Combine(directory, "jobs.db"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var request = Request();
            store.Accept(request).AlreadyAccepted.Should().BeFalse();
            store.Accept(request).AlreadyAccepted.Should().BeTrue();
            var conflicting = request with { MinimumTrainingSamples = 51, InputHash = string.Empty };
            conflicting = conflicting with { InputHash = MlTrainingContractHash.Input(conflicting) };
            var action = () => store.Accept(conflicting);
            action.Should().Throw<InvalidOperationException>()
                .WithMessage("*job-id-input-conflict*");
            store.Cancel(request.JobId).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MlTrainingJobRequest Request()
    {
        var cutoff = new DateTime(2026, 8, 20, 20, 0, 0, DateTimeKind.Utc);
        var regimes = Enumerable.Range(0, 120).Select(index =>
        {
            var group = index % 4;
            return new MlRegimeFeatureContract(cutoff.AddDays(index - 120),
                group switch { 0 => .02f, 1 => -.02f, 2 => 0f, _ => .01f },
                group switch { 0 => .05f, 1 => -.05f, 2 => .001f, _ => .02f },
                group switch { 0 => .12f, 1 => -.12f, 2 => .002f, _ => .03f },
                group == 3 ? .2f : .02f, group * .1f,
                group switch { 0 => .08f, 1 => -.08f, _ => 0f }, .3f + group * .15f);
        }).OrderBy(x => x.AsOfUtc).ToArray();
        var evidence = new MlRegimeDataEvidenceContract("Alpaca", "SPY", "Daily",
            "SplitAdjusted", "2024-2027.1", regimes[0].AsOfUtc,
            regimes[^1].AsOfUtc, new string('a', 64), new string('b', 64));
        var draft = new MlTrainingJobRequest(MlTrainingContractVersions.Current,
            "ml-conformance", string.Empty, MlTrainingContractVersions.Trainer,
            MlTrainingContractVersions.RegimeFeatureSchema,
            MlTrainingContractVersions.SignalFeatureSchema, cutoff, cutoff,
            50, MlTrainingContractVersions.RequiredRegimeClusters,
            evidence, regimes, []);
        return draft with { InputHash = MlTrainingContractHash.Input(draft) };
    }
}
