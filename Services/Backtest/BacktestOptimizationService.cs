using System.Diagnostics;
using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Services.Backtest;

/// <summary>파라미터 후보 준비, IS/OOS 실행과 결과 랭킹을 조정하는 최적화 유스케이스입니다.</summary>
public sealed class BacktestOptimizationService
{
    private readonly IOptimizationEvaluationContextPreparer _contextPreparer;
    private readonly IOptimizationCandidateEvaluator _candidateEvaluator;
    private readonly ILogger<BacktestOptimizationService> _logger;

    public BacktestOptimizationService(
        IOptimizationEvaluationContextPreparer contextPreparer,
        IOptimizationCandidateEvaluator candidateEvaluator,
        ILogger<BacktestOptimizationService> logger)
    {
        _contextPreparer = contextPreparer;
        _candidateEvaluator = candidateEvaluator;
        _logger = logger;
    }

    public async Task<OptimizeResponse> RunAsync(
        OptimizeRequest request,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var allCombinations = StrategyOptimizationSpace.GenerateOptimizeCombinations(
            request.OptimizeParams);
        var totalCombinations = allCombinations.Count;
        var searchPlan = OptimizationJobExecutionPolicy.BuildSearchPlan(
            allCombinations, request.MaxCombinations);
        var coarseCombinations = searchPlan.Stage1Combinations;
        var fineBudget = searchPlan.Stage2Budget;

        _logger.LogInformation(
            "파라미터 최적화 시작: 총 {Total}개 조합, Stage1={Stage1}개, Stage2 예산={Stage2}개, 심볼={Symbols}",
            totalCombinations,
            coarseCombinations.Count,
            fineBudget,
            string.Join(",", request.Symbols));

        var period = OptimizationJobExecutionPolicy.SplitPeriod(
            request.From, request.To, request.OosPercent);
        var preparation = await _contextPreparer.PrepareAsync(request, ct);
        if (!preparation.IsSuccess)
        {
            _logger.LogWarning("최적화 준비 실패: {Message}", preparation.Message);
            return Empty(totalCombinations, stopwatch.ElapsedMilliseconds);
        }
        var evaluation = preparation.Context!;
        var results = new List<OptimizeResultItem>(coarseCombinations.Count + fineBudget);
        results.AddRange(await _candidateEvaluator.EvaluateBatchAsync(
            evaluation,
            coarseCombinations,
            request.From,
            period.InSampleTo,
            "최적화 조합 백테스트 실패 — 건너뜀",
            ct));

        if (fineBudget > 0 && results.Count >= 3)
        {
            var seeds = OptimizationResultRanker.RankOptimizeResults(
                results,
                request.RankBy,
                OptimizationJobExecutionPolicy.FineSearchSeedCount);
            var neighbors = StrategyOptimizationSpace.GenerateNeighborCombinations(
                seeds.Select(result => result.Params).ToList(),
                request.OptimizeParams,
                fineBudget,
                coarseCombinations);
            neighbors = OptimizationJobExecutionPolicy.BuildStage2CandidatePool(
                neighbors,
                coarseCombinations,
                allCombinations,
                fineBudget);
            _logger.LogInformation(
                "Stage 2 정밀 탐색: {Count}개 이웃 조합 테스트", neighbors.Count);

            results.AddRange(await _candidateEvaluator.EvaluateBatchAsync(
                evaluation,
                neighbors,
                request.From,
                period.InSampleTo,
                "Stage 2 백테스트 실패",
                ct));
        }

        var ranked = OptimizationResultRanker.RankOptimizeResults(
            results, request.RankBy, request.MaxResults);
        if (period.HasOutOfSample)
        {
            foreach (var item in ranked)
            {
                var oos = await _candidateEvaluator.RunAsync(
                    evaluation,
                    item.Params,
                    period.OutOfSampleFrom,
                    period.OutOfSampleTo,
                    "OOS 백테스트 실패",
                    ct);
                if (oos is not null)
                    OptimizationResultProjection.ApplyOutOfSample(item, oos);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "파라미터 최적화 완료: {Tested}개 테스트, {ElapsedMs}ms",
            results.Count,
            stopwatch.ElapsedMilliseconds);
        return new OptimizeResponse
        {
            TotalCombinations = totalCombinations,
            TestedCombinations = results.Count,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Results = ranked,
            IsFrom = request.From,
            IsTo = period.InSampleTo,
            OosFrom = period.HasOutOfSample ? period.OutOfSampleFrom : null,
            OosTo = period.HasOutOfSample ? period.OutOfSampleTo : null
        };
    }

    private static OptimizeResponse Empty(int total, long elapsed) => new()
    {
        TotalCombinations = total,
        TestedCombinations = 0,
        ElapsedMs = elapsed
    };
}
