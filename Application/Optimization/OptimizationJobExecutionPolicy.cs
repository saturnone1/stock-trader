using System.Text.Json;

namespace StockTrader.Application.Optimization;

public sealed record OptimizationPeriodSplit(
    DateTime InSampleTo,
    DateTime OutOfSampleFrom,
    DateTime OutOfSampleTo,
    bool HasOutOfSample);

public sealed record OptimizationSearchPlan(
    List<OptimizeParamSnapshot> Stage1Combinations,
    int Stage2Budget);

/// <summary>
/// 백그라운드 워커와 저장소 수명에 무관한 최적화 작업의 기간·예산·재개 정책입니다.
/// 같은 요청과 작업 ID는 재시작 후에도 같은 탐색 순서와 청크 경계를 만듭니다.
/// </summary>
public static class OptimizationJobExecutionPolicy
{
    public const decimal InitialExplorationFraction = 0.60m;
    public const decimal MaximumOutOfSampleFraction = 0.50m;
    public const int FineSearchSeedCount = 5;

    public static OptimizationPeriodSplit SplitPeriod(
        DateTime from,
        DateTime to,
        decimal requestedOutOfSampleFraction)
    {
        var fraction = Math.Clamp(
            requestedOutOfSampleFraction, 0m, MaximumOutOfSampleFraction);
        var totalDays = (to - from).TotalDays;
        var inSampleTo = fraction > 0
            ? from.AddDays(totalDays * (double)(1m - fraction))
            : to;

        return new OptimizationPeriodSplit(
            inSampleTo,
            inSampleTo,
            to,
            fraction > 0 && inSampleTo < to);
    }

    public static OptimizationSearchPlan BuildSearchPlan(
        List<OptimizeParamSnapshot> allCombinations,
        int maxCombinations)
    {
        ArgumentNullException.ThrowIfNull(allCombinations);
        if (maxCombinations < 0)
            throw new ArgumentOutOfRangeException(nameof(maxCombinations));

        if (allCombinations.Count <= maxCombinations)
            return new OptimizationSearchPlan(allCombinations, 0);

        var stage1Budget = (int)(maxCombinations * InitialExplorationFraction);
        var stage2Budget = maxCombinations - stage1Budget;
        var stage1 = StrategyOptimizationSpace.SelectDeterministicSample(
            allCombinations, stage1Budget);
        return new OptimizationSearchPlan(stage1, stage2Budget);
    }

    public static bool HasExceededDuration(
        DateTime startedAt,
        DateTime observedAt,
        decimal? maxDurationHours) =>
        maxDurationHours.HasValue
        && observedAt - startedAt > TimeSpan.FromHours((double)maxDurationHours.Value);

    public static int CalculateStage1StartChunk(
        long testedCombinations,
        int stage1CombinationCount,
        int persistedChunkIndex,
        int totalChunks)
    {
        if (testedCombinations >= stage1CombinationCount)
            return totalChunks;

        return Math.Clamp(persistedChunkIndex, 0, totalChunks);
    }

    public static int CalculateStage2StartChunk(
        long testedCombinations,
        int stage1CombinationCount,
        int chunkSize)
    {
        var safeChunkSize = Math.Max(1, chunkSize);
        var processedStage2 = Math.Max(0L, testedCombinations - stage1CombinationCount);
        return (int)Math.Ceiling(processedStage2 / (double)safeChunkSize);
    }

    public static List<OptimizeParamSnapshot> BuildStage2CandidatePool(
        IEnumerable<OptimizeParamSnapshot> preferredCandidates,
        List<OptimizeParamSnapshot> stage1Combinations,
        List<OptimizeParamSnapshot> allCombinations,
        int budget)
    {
        if (budget <= 0) return [];

        static string SnapshotKey(OptimizeParamSnapshot snapshot) =>
            JsonSerializer.Serialize(snapshot);

        var selected = new List<OptimizeParamSnapshot>(budget);
        var seenKeys = new HashSet<string>(stage1Combinations.Select(SnapshotKey));

        void TryAdd(OptimizeParamSnapshot candidate)
        {
            if (selected.Count >= budget) return;
            if (seenKeys.Add(SnapshotKey(candidate))) selected.Add(candidate);
        }

        foreach (var candidate in preferredCandidates)
        {
            TryAdd(candidate);
            if (selected.Count >= budget) return selected;
        }

        foreach (var candidate in allCombinations)
        {
            TryAdd(candidate);
            if (selected.Count >= budget) break;
        }

        return selected;
    }
}
