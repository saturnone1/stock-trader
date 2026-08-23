namespace StockTrader.Application.Optimization;

public static class OptimizationShadowEligibilityPolicy
{
    /// <summary>제한·재개 작업은 전체 원격 실행과 동일 비교 집합이 아니므로 제외합니다.</summary>
    public static bool CanCompare(OptimizationJobExecutionTicket job) =>
        job.TestedCombinations == 0
        && job.CurrentChunkIndex == 0
        && job.MaxDurationHours is null
        && job.MaxTestedCombinations is null;

    public static OptimizeRequest CreateComparableRequest(
        OptimizeRequest request,
        OptimizationJobExecutionTicket job)
    {
        var comparable = OptimizeRequestJsonCodec.Clone(request);
        comparable.RankBy = job.RankBy;
        comparable.MaxResults = job.TopResultsToKeep;
        return comparable;
    }
}
