using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Optimization;
using StockTrader.Models;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Data.Repositories;

public sealed partial class OptimizationWorkerLeaseCoordinator
{
    public async Task<OptimizationWorkerResultReceipt> SubmitResultAsync(
        string workerId,
        OptimizationWorkerResultSubmission submission,
        DateTime observedAt,
        CancellationToken ct)
    {
        var now = Utc(observedAt);
        if (!_transport.LeaseTransportEnabled)
            return Result(OptimizationResultAcceptance.StaleLease);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await db.OptimizationWorkerLeases.AsNoTracking()
            .SingleOrDefaultAsync(lease => lease.LeaseId == submission.LeaseId, ct);
        if (record is null || record.WorkerId != workerId)
            return Result(OptimizationResultAcceptance.StaleLease);

        if (record.Status == OptimizationWorkerLeaseStatus.Completed
            && (record.SubmissionId != submission.SubmissionId
                || record.ResultHash != submission.ResultHash))
            return Result(OptimizationResultAcceptance.StaleLease);
        var duplicate = record.Status == OptimizationWorkerLeaseStatus.Completed;
        var acceptance = OptimizationResultAcceptancePolicy.Evaluate(
            ToLease(record), submission, record.CancellationGeneration, duplicate, now);
        if (acceptance != OptimizationResultAcceptance.Accepted)
            return Result(acceptance);
        if (!IsMatchingResult(record, submission.ResultJson))
            return Result(OptimizationResultAcceptance.InvalidResultPayload);

        var affected = await db.OptimizationWorkerLeases
            .Where(lease => lease.LeaseId == record.LeaseId
                && lease.Status == OptimizationWorkerLeaseStatus.Leased
                && lease.WorkerId == workerId
                && lease.LeaseGeneration == record.LeaseGeneration
                && lease.CancellationGeneration == record.CancellationGeneration)
            .ExecuteUpdateAsync(update => update
                .SetProperty(lease => lease.Status, OptimizationWorkerLeaseStatus.Completed)
                .SetProperty(lease => lease.SubmissionId, submission.SubmissionId)
                .SetProperty(lease => lease.ResultHash, submission.ResultHash)
                .SetProperty(lease => lease.ResultJson, submission.ResultJson)
                .SetProperty(lease => lease.CompletedAt, now),
                ct);
        if (affected == 1) return Result(OptimizationResultAcceptance.Accepted);

        var existingSubmission = await db.OptimizationWorkerLeases.AsNoTracking()
            .Where(lease => lease.LeaseId == record.LeaseId)
            .Select(lease => lease.SubmissionId)
            .SingleOrDefaultAsync(ct);
        return Result(existingSubmission == submission.SubmissionId
            ? OptimizationResultAcceptance.Duplicate
            : OptimizationResultAcceptance.StaleLease);
    }

    private static bool IsMatchingResult(
        OptimizationWorkerLeaseRecord record,
        string resultJson) => record.Purpose switch
        {
            OptimizationWorkerContractCatalog.ShadowValidationPurpose =>
                IsMatchingValidationResult(record, resultJson),
            OptimizationWorkerContractCatalog.ShadowComputePurpose =>
                IsMatchingComputeResult(record, resultJson),
            _ => false
        };

    private static bool IsMatchingValidationResult(
        OptimizationWorkerLeaseRecord record,
        string resultJson)
    {
        try
        {
            var result = JsonSerializer.Deserialize<OptimizationWorkerValidationResult>(
                resultJson, JsonOptions);
            var input = DeserializeInput(record.InputJson);
            return result is not null
                && result.ContractVersion == OptimizationWorkerContractCatalog.ResultVersion
                && result.Purpose == record.Purpose
                && result.InputHash == input.InputHash
                && result.StrategyHash == input.Strategy.ContentHash
                && result.EvidenceId == input.DataEvidence.EvidenceId
                && result.PreparedDataHash == input.PreparedData.DataHash
                && result.SeriesCount == input.PreparedData.Series.Count
                && result.BarCount == input.PreparedData.Series.Sum(series => series.Bars.Count);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsMatchingComputeResult(
        OptimizationWorkerLeaseRecord record,
        string resultJson)
    {
        try
        {
            var result = JsonSerializer.Deserialize<OptimizationWorkerComputeResult>(
                resultJson, JsonOptions);
            var input = DeserializeInput(record.InputJson);
            var request = OptimizeRequestJsonCodec.Deserialize(input.RequestJson);
            return result is not null
                && request is not null
                && result.ContractVersion == OptimizationWorkerContractCatalog.ResultVersion
                && result.Purpose == record.Purpose
                && result.InputHash == input.InputHash
                && result.TotalCombinations >= 0
                && result.TestedCombinations >= 0
                && result.TestedCombinations <= result.TotalCombinations
                && result.ElapsedMs >= 0
                && result.Results.Count <= request.MaxResults
                && result.Results.All(IsValidCandidate);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidCandidate(OptimizationWorkerCandidateResult result)
    {
        if (result.Rank < 1 || result.TotalTrades < 0 || result.OosTotalTrades < 0
            || string.IsNullOrWhiteSpace(result.ParametersJson))
            return false;
        try
        {
            using var parameters = JsonDocument.Parse(result.ParametersJson);
            return parameters.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static OptimizationWorkerResultReceipt Result(
        OptimizationResultAcceptance acceptance) => new(
        OptimizationWorkerContractCatalog.ResultVersion,
        acceptance);
}
