using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
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
        lease.Purpose.Should().Be(OptimizationWorkerContractCatalog.OptimizationComputePurpose);
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
        stored.ComparisonStatus.Should().Be(OptimizationShadowComparisonStatus.AwaitingAuthoritative);

        await fixture.Store.RecordAuthoritativeAsync(lease.JobId, Now.AddSeconds(8), default);
        await db.Entry(stored).ReloadAsync();
        stored.ComparisonStatus.Should().Be(OptimizationShadowComparisonStatus.Match);
        stored.AuthoritativeResultHash.Should().NotBeNullOrWhiteSpace();
        stored.ComparedAt.Should().Be(Now.AddSeconds(8));
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
        var reclaimed = await fixture.Store.GetOperationalSummaryAsync(
            Now.AddSeconds(32), default);
        reclaimed.Pending.Should().Be(0);
        reclaimed.Active.Should().Be(1);
        reclaimed.ExpiredActive.Should().Be(0);
        reclaimed.Reclaimed.Should().Be(1);

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
        var cancelled = await fixture.Store.GetOperationalSummaryAsync(
            Now.AddSeconds(34), default);
        cancelled.Active.Should().Be(0);
        cancelled.Cancelled.Should().Be(1);
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

    [Fact]
    public async Task RemoteAuthority_CommitsCanonicalResultExactlyOnce()
    {
        await using var fixture = await LeaseFixture.CreateAsync(
            mode: OptimizationWorkerTransportMode.Remote);
        var publication = await fixture.Store.PublishRemoteAsync(1, Input(), Now, default);
        var lease = await fixture.Store.TryLeaseAsync("worker-a", Now.AddSeconds(1), default);
        lease.Should().NotBeNull();
        lease!.LeaseId.Should().Be(publication.LeaseId);

        var submission = Submission(lease, Now.AddSeconds(2));
        (await fixture.Store.SubmitResultAsync(
            "worker-a", submission, Now.AddSeconds(2), default)).Acceptance
            .Should().Be(OptimizationResultAcceptance.Accepted);
        (await fixture.Store.TryCommitAsync(
            1, publication.LeaseId, publication.InputHash, Now.AddSeconds(3), default))
            .Should().Be(OptimizationRemoteCommitOutcome.Committed);
        (await fixture.SecondStore.TryCommitAsync(
            1, publication.LeaseId, publication.InputHash, Now.AddSeconds(4), default))
            .Should().Be(OptimizationRemoteCommitOutcome.AlreadyCommitted);

        await using var db = await fixture.Factory.CreateDbContextAsync();
        var stored = await db.OptimizationWorkerLeases.SingleAsync();
        stored.Authority.Should().Be(OptimizationWorkerLeaseAuthority.Canonical);
        stored.CanonicalCommittedAt.Should().Be(Now.AddSeconds(3));
        stored.CanonicalResultHash.Should().NotBeNullOrWhiteSpace();
        var summary = await fixture.Store.GetOperationalSummaryAsync(
            Now.AddSeconds(4), default);
        summary.CanonicalCompleted.Should().Be(1);
        summary.CanonicalCommitted.Should().Be(1);
    }

    [Fact]
    public async Task RemoteAuthority_CancelInvalidatesCurrentGeneration()
    {
        await using var fixture = await LeaseFixture.CreateAsync(
            mode: OptimizationWorkerTransportMode.Remote);
        var publication = await fixture.Store.PublishRemoteAsync(1, Input(), Now, default);
        var lease = (await fixture.Store.TryLeaseAsync(
            "worker-a", Now.AddSeconds(1), default))!;

        await fixture.Store.CancelRemoteAsync(
            1, publication.LeaseId, Now.AddSeconds(2), default);

        var stopped = await fixture.Store.HeartbeatAsync(
            "worker-a", Heartbeat(lease, Now.AddSeconds(3)), Now.AddSeconds(3), default);
        stopped.Continue.Should().BeFalse();
        stopped.Reason.Should().Be("lease-cancelled");
        stopped.CancellationGeneration.Should().Be(1);
        (await fixture.Store.SubmitResultAsync(
            "worker-a", Submission(lease, Now.AddSeconds(3)), Now.AddSeconds(3), default))
            .Acceptance.Should().Be(OptimizationResultAcceptance.CancelledGeneration);
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
        var result = new OptimizationWorkerComputeResult(
            OptimizationWorkerContractCatalog.ResultVersion,
            lease.Purpose,
            lease.Input.InputHash,
            1,
            1,
            10,
            Now.AddDays(-2),
            Now.AddDays(-1),
            null,
            null,
            []);
        var resultJson = JsonSerializer.Serialize(
            result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new(
            OptimizationWorkerContractCatalog.ResultVersion,
            lease.LeaseId + ":compute:v1",
            lease.LeaseId,
            lease.JobId,
            lease.LeaseGeneration,
            lease.CancellationGeneration,
            lease.Input.InputHash,
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
            bar.Timestamp, bar.Timestamp, 1, OptimizationDataCompleteness.Unverified, "bars")
        {
            MarketTimeZoneId = "America/New_York",
            WarmupCalendarDays = 300,
            RequiredWarmupBars = 200
        };
        var evidence = new OptimizationDataEvidenceSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            OptimizationDataEvidenceIdentity.Compute([evidenceSeries]),
            [evidenceSeries]);
        var requestJson = OptimizeRequestJsonCodec.Serialize(new OptimizeRequest
        {
            BasePattern = new StrategyDocument
            {
                Name = "lease-test",
                EntryRulesJson = artifact.StrategyDocumentJson
            },
            Symbols = ["TQQQ"],
            From = Now.AddDays(-2),
            To = Now.AddDays(-1),
            OosPercent = 0,
            MaxResults = 10
        });
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

        public static async Task<LeaseFixture> CreateAsync(
            int leaseSeconds = 300,
            OptimizationWorkerTransportMode mode = OptimizationWorkerTransportMode.Shadow)
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
                    TotalCombinations = 1,
                    TestedCombinations = 1,
                    CreatedAt = Now
                });
                await db.SaveChangesAsync();
            }
            var configured = Options.Create(new OptimizationWorkerTransportOptions
            {
                Enabled = true,
                LeaseTransportEnabled = true,
                Mode = mode,
                SharedSecret = new string('x', 32),
                LeaseSeconds = leaseSeconds
            });
            return new(path, factory,
                new OptimizationWorkerLeaseCoordinator(
                    factory, configured, NullLogger<OptimizationWorkerLeaseCoordinator>.Instance),
                new OptimizationWorkerLeaseCoordinator(
                    factory, configured, NullLogger<OptimizationWorkerLeaseCoordinator>.Instance));
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
