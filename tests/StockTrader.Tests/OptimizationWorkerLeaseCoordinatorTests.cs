using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Tests;

public sealed class OptimizationWorkerLeaseCoordinatorTests
{
    private static readonly DateTime Now =
        new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublishAndClaim_AreDurableIdempotentAndSingleOwner()
    {
        await using var fixture = await LeaseFixture.CreateAsync();
        var input = Input();

        await fixture.Store.PublishShadowAsync(1, input, Now, default);
        await fixture.Store.PublishShadowAsync(1, input, Now.AddSeconds(1), default);

        await using (var db = await fixture.Factory.CreateDbContextAsync())
            (await db.OptimizationWorkerLeases.CountAsync()).Should().Be(1);

        var claims = await Task.WhenAll(
            fixture.Store.TryLeaseAsync("worker-a", Now.AddSeconds(2), default),
            fixture.SecondStore.TryLeaseAsync("worker-b", Now.AddSeconds(2), default));

        claims.Should().ContainSingle(lease => lease != null);
        var lease = claims.Single(item => item is not null)!;
        lease.Input.InputHash.Should().Be(input.InputHash);
        lease.Purpose.Should().Be(OptimizationWorkerContractCatalog.ShadowValidationPurpose);
        lease.LeaseGeneration.Should().Be(1);
    }

    [Fact]
    public async Task HeartbeatAndResult_AcceptOnlyCurrentOwnerAndAreIdempotent()
    {
        await using var fixture = await LeaseFixture.CreateAsync();
        var lease = await fixture.PublishAndLeaseAsync(Now);
        var heartbeat = Heartbeat(lease, Now.AddSeconds(5));

        var wrongOwner = await fixture.Store.HeartbeatAsync(
            "other-worker", heartbeat, Now.AddSeconds(5), default);
        wrongOwner.Continue.Should().BeFalse();
        wrongOwner.Reason.Should().Be("stale-lease");

        var heartbeatReceipt = await fixture.Store.HeartbeatAsync(
            "worker-a", heartbeat, Now.AddSeconds(5), default);
        heartbeatReceipt.Continue.Should().BeTrue();
        heartbeatReceipt.LeaseExpiresAt.Should().BeAfter(lease.ExpiresAt);

        var submission = Submission(lease, Now.AddSeconds(6));
        (await fixture.Store.SubmitResultAsync(
            "worker-a", submission, Now.AddSeconds(6), default)).Acceptance
            .Should().Be(OptimizationResultAcceptance.Accepted);
        (await fixture.Store.SubmitResultAsync(
            "worker-a", submission, Now.AddMinutes(10), default)).Acceptance
            .Should().Be(OptimizationResultAcceptance.Duplicate);
        (await fixture.Store.SubmitResultAsync(
            "worker-a", submission with { SubmissionId = "different" },
            Now.AddSeconds(7), default)).Acceptance
            .Should().Be(OptimizationResultAcceptance.StaleLease);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var stored = await db.OptimizationWorkerLeases.SingleAsync();
        stored.Status.Should().Be(OptimizationWorkerLeaseStatus.Completed);
        stored.SubmissionId.Should().Be(submission.SubmissionId);
    }

    [Fact]
    public async Task ExpiryCancellationAndPayloadValidation_FailClosed()
    {
        await using var fixture = await LeaseFixture.CreateAsync(leaseSeconds: 30);
        var first = await fixture.PublishAndLeaseAsync(Now);
        var second = await fixture.SecondStore.TryLeaseAsync(
            "worker-b", Now.AddSeconds(31), default);
        second.Should().NotBeNull();
        second!.LeaseGeneration.Should().Be(2);

        var staleSubmission = Submission(first, Now.AddSeconds(32));
        (await fixture.Store.SubmitResultAsync(
            "worker-a", staleSubmission, Now.AddSeconds(32), default)).Acceptance
            .Should().Be(OptimizationResultAcceptance.StaleLease);

        var invalid = Submission(second, Now.AddSeconds(32)) with
        {
            ResultJson = JsonSerializer.Serialize(new { invalid = true })
        };
        invalid = invalid with { ResultHash = CanonicalJsonHash.Compute(invalid.ResultJson) };
        (await fixture.Store.SubmitResultAsync(
            "worker-b", invalid, Now.AddSeconds(32), default)).Acceptance
            .Should().Be(OptimizationResultAcceptance.InvalidResultPayload);

        await using (var db = await fixture.Factory.CreateDbContextAsync())
        {
            var job = await db.OptimizationJobs.SingleAsync();
            job.Status = OptimizationJobStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        var stopped = await fixture.Store.HeartbeatAsync(
            "worker-b", Heartbeat(second, Now.AddSeconds(33)),
            Now.AddSeconds(33), default);
        stopped.Continue.Should().BeFalse();
        stopped.Reason.Should().Be("job-stopped");
        stopped.CancellationGeneration.Should().Be(1);
    }

    [Fact]
    public void HeartbeatPolicy_RejectsNegativeProgressAndMismatchedIdentity()
    {
        var input = Input();
        var lease = new OptimizationWorkLease(
            OptimizationWorkerContractCatalog.LeaseVersion,
            "lease-1",
            1,
            2,
            3,
            Now,
            Now.AddMinutes(1),
            input);

        OptimizationHeartbeatAcceptancePolicy.Evaluate(
            lease, Heartbeat(lease, Now.AddSeconds(1)) with { TestedCombinations = -1 },
            3, Now.AddSeconds(1)).Should().Be(OptimizationHeartbeatAcceptance.InvalidProgress);
        OptimizationHeartbeatAcceptancePolicy.Evaluate(
            lease, Heartbeat(lease, Now.AddSeconds(1)) with { InputHash = "changed" },
            3, Now.AddSeconds(1)).Should().Be(OptimizationHeartbeatAcceptance.InputMismatch);
    }

    private static OptimizationWorkerHeartbeat Heartbeat(
        OptimizationWorkLease lease,
        DateTime at) => new(
        OptimizationWorkerContractCatalog.HeartbeatVersion,
        lease.LeaseId,
        lease.JobId,
        lease.LeaseGeneration,
        lease.CancellationGeneration,
        lease.Input.InputHash,
        0,
        at);

    private static OptimizationWorkerResultSubmission Submission(
        OptimizationWorkLease lease,
        DateTime at)
    {
        var input = lease.Input;
        var result = new OptimizationWorkerValidationResult(
            OptimizationWorkerContractCatalog.ResultVersion,
            lease.Purpose,
            input.InputHash,
            input.Strategy.ContentHash,
            input.DataEvidence.EvidenceId,
            input.PreparedData.DataHash,
            input.PreparedData.Series.Count,
            input.PreparedData.Series.Sum(series => series.Bars.Count));
        var resultJson = JsonSerializer.Serialize(
            result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new(
            OptimizationWorkerContractCatalog.ResultVersion,
            lease.LeaseId + ":validation:v1",
            lease.LeaseId,
            lease.JobId,
            lease.LeaseGeneration,
            lease.CancellationGeneration,
            input.InputHash,
            CanonicalJsonHash.Compute(resultJson),
            resultJson,
            at);
    }

    private static OptimizationEvaluationInput Input()
    {
        var artifact = StrategyExecutionArtifactFactory.Create(new StrategyDocument
        {
            Name = "lease-test",
            EntryRulesJson = "[{\"indicator\":\"RSI\",\"params\":{\"period\":2},\"operator\":\"<=\",\"value\":30}]",
            AtrStopMultiplier = 2,
            AtrTargetMultiplier = 4,
            MaxHoldingBars = 10
        });
        var bar = new OptimizationBar(Now.AddDays(-1), 100, 101, 99, 100, 1000, 100);
        var series = new OptimizationPreparedSeries(
            "TQQQ", "Daily", [bar], [1], [100], [0], [0], [0]);
        var risk = new OptimizationRiskSnapshot(1, 3, 5, 2);
        var prepared = new OptimizationPreparedDataSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            OptimizationPreparedDataIdentity.Compute([series], [], risk),
            [series], [], risk);
        var evidenceSeries = new OptimizationSymbolDataEvidence(
            "TQQQ", "Daily", "Alpaca", "UnitedStates", "SplitAdjusted",
            "RegularSessionOnly", "us-equities-v1", Now.AddDays(-2), Now,
            bar.Timestamp, bar.Timestamp, 1, OptimizationDataCompleteness.Unverified, "bars");
        var evidence = new OptimizationDataEvidenceSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            OptimizationDataEvidenceIdentity.Compute([evidenceSeries]),
            [evidenceSeries]);
        const string requestJson = "{}";
        var inputHash = OptimizationEvaluationInputIdentity.Compute(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            requestJson, artifact.ContentHash, evidence.EvidenceId, prepared.DataHash);
        return new(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            inputHash, requestJson, artifact, evidence, prepared);
    }

    private sealed class LeaseFixture : IAsyncDisposable
    {
        private readonly string _path;
        public IDbContextFactory<AppDbContext> Factory { get; }
        public OptimizationWorkerLeaseCoordinator Store { get; }
        public OptimizationWorkerLeaseCoordinator SecondStore { get; }

        private LeaseFixture(
            string path,
            IDbContextFactory<AppDbContext> factory,
            OptimizationWorkerLeaseCoordinator store,
            OptimizationWorkerLeaseCoordinator secondStore)
        {
            _path = path;
            Factory = factory;
            Store = store;
            SecondStore = secondStore;
        }

        public static async Task<LeaseFixture> CreateAsync(int leaseSeconds = 300)
        {
            var path = Path.Combine(Path.GetTempPath(), $"stocktrader-lease-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False")
                .Options;
            var factory = new TestDbContextFactory(options);
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.OptimizationJobs.Add(new OptimizationJob
                {
                    Id = 1,
                    Name = "lease-test",
                    Status = OptimizationJobStatus.Running,
                    RequestJson = "{}",
                    CreatedAt = Now
                });
                await db.SaveChangesAsync();
            }
            var configured = Options.Create(new OptimizationWorkerTransportOptions
            {
                Enabled = true,
                LeaseTransportEnabled = true,
                Mode = OptimizationWorkerTransportMode.Shadow,
                SharedSecret = new string('x', 32),
                LeaseSeconds = leaseSeconds
            });
            return new(path, factory,
                new OptimizationWorkerLeaseCoordinator(factory, configured),
                new OptimizationWorkerLeaseCoordinator(factory, configured));
        }

        public async Task<OptimizationWorkLease> PublishAndLeaseAsync(DateTime at)
        {
            await Store.PublishShadowAsync(1, Input(), at, default);
            return (await Store.TryLeaseAsync("worker-a", at, default))!;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_path)) File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
