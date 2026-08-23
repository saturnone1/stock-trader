using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StockTrader.Application.Optimization;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Data.Repositories;

/// <summary>
/// Strategy Research 데이터베이스에서 임대를 단독 소유합니다. Worker는 이 어댑터나
/// 데이터베이스를 공유하지 않으며 인증된 API를 통해서만 상태를 전이합니다.
/// </summary>
public sealed partial class OptimizationWorkerLeaseCoordinator
    : IOptimizationWorkerLeaseCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly OptimizationWorkerTransportOptions _transport;
    private readonly ILogger<OptimizationWorkerLeaseCoordinator> _logger;

    public OptimizationWorkerLeaseCoordinator(
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<OptimizationWorkerTransportOptions> transport,
        ILogger<OptimizationWorkerLeaseCoordinator> logger)
    {
        _dbFactory = dbFactory;
        _transport = transport.Value;
        _logger = logger;
    }

    public async Task PublishShadowAsync(
        int jobId,
        OptimizationEvaluationInput input,
        DateTime observedAt,
        CancellationToken ct)
    {
        if (!_transport.LeaseTransportEnabled
            || _transport.Mode != OptimizationWorkerTransportMode.Shadow)
            return;

        var now = Utc(observedAt);
        var computeLease = new OptimizationWorkLease(
            OptimizationWorkerContractCatalog.LeaseVersion,
            "validation",
            jobId,
            1,
            0,
            now,
            now.AddSeconds(_transport.LeaseSeconds),
            input)
        {
            Purpose = OptimizationWorkerContractCatalog.ShadowComputePurpose
        };
        var compatibilityError = OptimizationLeaseCompatibilityPolicy.Error(computeLease)
            ?? StrategyExecutionArtifactPolicy.CompatibilityError(input.Strategy);
        if (compatibilityError is not null)
            throw new InvalidOperationException(
                $"Optimization shadow input is incompatible: {compatibilityError}");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var exists = await db.OptimizationWorkerLeases.AsNoTracking().AnyAsync(
            lease => lease.JobId == jobId
                && lease.Purpose == OptimizationWorkerContractCatalog.ShadowComputePurpose
                && lease.InputHash == input.InputHash,
            ct);
        if (exists) return;

        db.OptimizationWorkerLeases.Add(new OptimizationWorkerLeaseRecord
        {
            LeaseId = $"opt-{jobId}-{Guid.NewGuid():N}",
            JobId = jobId,
            Purpose = OptimizationWorkerContractCatalog.ShadowComputePurpose,
            InputHash = input.InputHash,
            InputJson = JsonSerializer.Serialize(input, JsonOptions),
            Status = OptimizationWorkerLeaseStatus.Pending,
            ComparisonStatus = OptimizationShadowComparisonStatus.AwaitingBoth,
            CreatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (!await db.OptimizationWorkerLeases.AsNoTracking().AnyAsync(
                    lease => lease.JobId == jobId
                        && lease.Purpose == OptimizationWorkerContractCatalog.ShadowComputePurpose
                        && lease.InputHash == input.InputHash,
                    ct))
                throw;
        }
    }

    public async Task<OptimizationWorkLease?> TryLeaseAsync(
        string workerId,
        DateTime observedAt,
        CancellationToken ct)
    {
        if (!_transport.LeaseTransportEnabled || string.IsNullOrWhiteSpace(workerId)) return null;
        var now = Utc(observedAt);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var candidate = await db.OptimizationWorkerLeases.AsNoTracking()
                .Where(lease => lease.Status == OptimizationWorkerLeaseStatus.Pending
                    || lease.Status == OptimizationWorkerLeaseStatus.Leased
                        && lease.ExpiresAt <= now)
                .OrderBy(lease => lease.CreatedAt)
                .Select(lease => new
                {
                    lease.LeaseId,
                    lease.Status,
                    lease.LeaseGeneration,
                    lease.ExpiresAt
                })
                .FirstOrDefaultAsync(ct);
            if (candidate is null) return null;

            var generation = candidate.LeaseGeneration + 1;
            var expiresAt = now.AddSeconds(_transport.LeaseSeconds);
            var claimed = await db.OptimizationWorkerLeases
                .Where(lease => lease.LeaseId == candidate.LeaseId
                    && lease.Status == candidate.Status
                    && lease.LeaseGeneration == candidate.LeaseGeneration
                    && lease.ExpiresAt == candidate.ExpiresAt)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(lease => lease.Status, OptimizationWorkerLeaseStatus.Leased)
                    .SetProperty(lease => lease.WorkerId, workerId)
                    .SetProperty(lease => lease.LeaseGeneration, generation)
                    .SetProperty(lease => lease.LeasedAt, now)
                    .SetProperty(lease => lease.ExpiresAt, expiresAt)
                    .SetProperty(lease => lease.LastHeartbeatAt, (DateTime?)null),
                    ct);
            if (claimed == 0) continue;

            var record = await db.OptimizationWorkerLeases.AsNoTracking()
                .SingleAsync(lease => lease.LeaseId == candidate.LeaseId, ct);
            return ToLease(record);
        }

        return null;
    }

    private static OptimizationWorkLease ToLease(OptimizationWorkerLeaseRecord record) => new(
        OptimizationWorkerContractCatalog.LeaseVersion,
        record.LeaseId,
        record.JobId,
        record.LeaseGeneration,
        record.CancellationGeneration,
        Utc(record.LeasedAt ?? record.CreatedAt),
        Utc(record.ExpiresAt ?? record.CreatedAt),
        DeserializeInput(record.InputJson))
    {
        Purpose = record.Purpose
    };

    private static OptimizationEvaluationInput DeserializeInput(string json) =>
        JsonSerializer.Deserialize<OptimizationEvaluationInput>(json, JsonOptions)
        ?? throw new InvalidOperationException("Stored optimization input is empty.");

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
