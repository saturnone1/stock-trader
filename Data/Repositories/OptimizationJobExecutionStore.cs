using System.Text.Json;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>최적화 작업 포트를 기존 SQLite 저장소와 영속 엔티티로 변환합니다.</summary>
public sealed class OptimizationJobExecutionStore : IOptimizationJobExecutionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOptimizationRepository _repository;

    public OptimizationJobExecutionStore(IOptimizationRepository repository)
    {
        _repository = repository;
    }

    public async Task<OptimizationJobControlSignal> GetControlSignalAsync(int jobId) =>
        await _repository.GetJobStatusAsync(jobId) switch
        {
            OptimizationJobStatus.Paused => OptimizationJobControlSignal.Pause,
            OptimizationJobStatus.Cancelled => OptimizationJobControlSignal.Cancel,
            _ => OptimizationJobControlSignal.Continue
        };

    public Task SaveProgressAsync(
        int jobId,
        long testedCombinations,
        int currentChunkIndex,
        DateTime? observedAt,
        long? totalCombinations = null) =>
        _repository.UpdateJobProgressAsync(
            jobId,
            testedCombinations,
            currentChunkIndex,
            observedAt,
            totalCombinations);

    public async Task SaveChunkAsync(
        int jobId,
        IReadOnlyList<OptimizeResultItem> results,
        long testedAtStart,
        long testedCombinations,
        int currentChunkIndex,
        DateTime observedAt,
        int topResultsToKeep,
        string rankBy)
    {
        var entities = results
            .Select((result, index) => ToEntity(
                result,
                jobId,
                testedAtStart + index,
                observedAt))
            .ToList();
        await _repository.CommitChunkAsync(
            jobId,
            entities,
            topResultsToKeep,
            rankBy,
            testedCombinations,
            currentChunkIndex,
            observedAt);
    }

    public async Task<IReadOnlyList<StoredOptimizationCandidate>> LoadTopCandidatesAsync(
        int jobId,
        int count)
    {
        var stored = await _repository.GetResultsAsync(jobId, count);
        var candidates = new List<StoredOptimizationCandidate>(stored.Count);
        foreach (var result in stored)
        {
            try
            {
                var parameters = JsonSerializer.Deserialize<OptimizeParamSnapshot>(
                    result.ParamsJson, JsonOptions);
                if (parameters is not null)
                    candidates.Add(new StoredOptimizationCandidate(result.Id, parameters));
            }
            catch (Exception)
            {
                // 오래된 손상 행은 실행기에서 건너뛰던 기존 호환 동작을 유지합니다.
            }
        }

        return candidates;
    }

    public Task SaveOutOfSampleAsync(
        int resultId,
        OptimizationPerformanceMetrics metrics) =>
        _repository.UpdateResultOutOfSampleAsync(resultId, metrics);

    private static OptimizationResult ToEntity(
        OptimizeResultItem item,
        int jobId,
        long testedAtCombination,
        DateTime discoveredAt) => new()
    {
        JobId = jobId,
        ParamsJson = JsonSerializer.Serialize(item.Params),
        TotalReturn = item.TotalReturn,
        SortinoRatio = item.SortinoRatio,
        SharpeRatio = item.SharpeRatio,
        MaxDrawdown = item.MaxDrawdown,
        WinRate = item.WinRate,
        TotalTrades = item.TotalTrades,
        ProfitFactor = item.ProfitFactor,
        CalmarRatio = item.CalmarRatio,
        AnnualizedReturn = item.AnnualizedReturn,
        TestedAtCombination = testedAtCombination,
        DiscoveredAt = discoveredAt
    };
}
