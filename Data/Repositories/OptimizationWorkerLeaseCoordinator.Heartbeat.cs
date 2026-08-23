using Microsoft.EntityFrameworkCore;
using StockTrader.Models;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Data.Repositories;

public sealed partial class OptimizationWorkerLeaseCoordinator
{
    public async Task<OptimizationWorkerHeartbeatReceipt> HeartbeatAsync(
        string workerId,
        OptimizationWorkerHeartbeat heartbeat,
        DateTime observedAt,
        CancellationToken ct)
    {
        var now = Utc(observedAt);
        if (!_transport.LeaseTransportEnabled)
            return StopHeartbeat(heartbeat.CancellationGeneration, "transport-disabled", now);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await db.OptimizationWorkerLeases.AsNoTracking()
            .SingleOrDefaultAsync(lease => lease.LeaseId == heartbeat.LeaseId, ct);
        if (record is null || record.Status != OptimizationWorkerLeaseStatus.Leased
            || record.WorkerId != workerId)
            return StopHeartbeat(heartbeat.CancellationGeneration, "stale-lease", now);

        var jobStatus = await db.OptimizationJobs.AsNoTracking()
            .Where(job => job.Id == record.JobId)
            .Select(job => (OptimizationJobStatus?)job.Status)
            .SingleOrDefaultAsync(ct);
        if (jobStatus is OptimizationJobStatus.Paused or OptimizationJobStatus.Cancelled)
        {
            var nextCancellation = record.CancellationGeneration + 1;
            await db.OptimizationWorkerLeases
                .Where(lease => lease.LeaseId == record.LeaseId
                    && lease.Status == OptimizationWorkerLeaseStatus.Leased
                    && lease.LeaseGeneration == record.LeaseGeneration)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(lease => lease.Status, OptimizationWorkerLeaseStatus.Cancelled)
                    .SetProperty(lease => lease.CancellationGeneration, nextCancellation)
                    .SetProperty(lease => lease.LastHeartbeatAt, now),
                    ct);
            return StopHeartbeat(nextCancellation, "job-stopped", now);
        }

        var acceptance = OptimizationHeartbeatAcceptancePolicy.Evaluate(
            ToLease(record), heartbeat, record.CancellationGeneration, now);
        if (acceptance != OptimizationHeartbeatAcceptance.Accepted)
            return StopHeartbeat(record.CancellationGeneration, acceptance.ToString(), now);

        var expiresAt = now.AddSeconds(_transport.LeaseSeconds);
        var affected = await db.OptimizationWorkerLeases
            .Where(lease => lease.LeaseId == record.LeaseId
                && lease.Status == OptimizationWorkerLeaseStatus.Leased
                && lease.WorkerId == workerId
                && lease.LeaseGeneration == record.LeaseGeneration
                && lease.CancellationGeneration == record.CancellationGeneration)
            .ExecuteUpdateAsync(update => update
                .SetProperty(lease => lease.LastHeartbeatAt, now)
                .SetProperty(lease => lease.ExpiresAt, expiresAt)
                .SetProperty(lease => lease.TestedCombinations, heartbeat.TestedCombinations),
                ct);
        return affected == 1
            ? new(OptimizationWorkerContractCatalog.HeartbeatVersion, true, expiresAt,
                record.CancellationGeneration, "accepted")
            : StopHeartbeat(record.CancellationGeneration, "stale-lease", now);
    }

    private static OptimizationWorkerHeartbeatReceipt StopHeartbeat(
        long cancellationGeneration,
        string reason,
        DateTime observedAt) => new(
        OptimizationWorkerContractCatalog.HeartbeatVersion,
        false,
        observedAt,
        cancellationGeneration,
        reason);
}
