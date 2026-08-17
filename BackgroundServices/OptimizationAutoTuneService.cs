using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockTrader.Api;
using StockTrader.Application.Optimization;
using StockTrader.Application.Strategies;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Services.Backtest;

namespace StockTrader.BackgroundServices;

public class OptimizationAutoTuneService
{
    public sealed record ApplyResultOutcome(
        bool Success,
        string Message,
        int? AppliedResultId,
        int AppliedResultCount);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OptimizationAutoTuneService> _logger;
    private readonly TimeProvider _clock;

    public OptimizationAutoTuneService(
        IServiceScopeFactory scopeFactory,
        ILogger<OptimizationAutoTuneService> logger,
        TimeProvider clock)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock;
    }

    public async Task HandleCompletedJobAsync(int jobId, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOptimizationRepository>();

        var job = await repo.GetJobSummaryAsync(jobId);
        if (job == null)
            return;

        var request = OptimizeRequestJsonCodec.Deserialize(job.RequestJson);
        if (request == null)
            return;

        if (job.AutoApplyBestResult)
            await ApplyResultAsync(job.Id, null, isAutoApply: true, ct);

        if (job.ContinuousMode)
            await RequeueContinuousJobAsync(job, request, repo, ct);
    }

    internal static OptimizationResult? SelectPromotionCandidate(
        IReadOnlyCollection<OptimizationResult> results,
        string rankBy,
        int minTrades)
    {
        if (results.Count == 0)
            return null;

        var hasOos = results.Any(r => r.OosTotalReturn.HasValue);
        var eligible = results
            .Where(r => GetTradeCount(r, hasOos) >= minTrades)
            .Where(r => GetTotalReturn(r, hasOos) > 0)
            .ToList();

        if (eligible.Count == 0)
            return null;

        return SortResults(eligible, rankBy, hasOos).FirstOrDefault();
    }

    internal static OptimizeRequest BuildNextRequest(
        OptimizeRequest currentRequest,
        StrategyDocument basePattern,
        DateTime utcNow)
    {
        var next = CloneOptimizeRequest(currentRequest);
        next.BasePattern = StrategyVariantFactory.CloneStrategyDocument(basePattern);

        var span = currentRequest.To - currentRequest.From;
        if (span <= TimeSpan.Zero)
            span = TimeSpan.FromDays(365);

        var candidateTo = utcNow.Date > currentRequest.To ? utcNow.Date : currentRequest.To;
        next.To = candidateTo;
        next.From = candidateTo - span;
        return next;
    }

    public async Task<ApplyResultOutcome> ApplyResultAsync(
        int jobId,
        int? resultId = null,
        bool isAutoApply = false,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOptimizationRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var patternManagement = scope.ServiceProvider.GetRequiredService<CustomPatternManagementService>();

        var job = await repo.GetJobSummaryAsync(jobId);
        if (job == null)
            return new ApplyResultOutcome(false, "최적화 Job을 찾을 수 없습니다.", null, 0);

        var request = OptimizeRequestJsonCodec.Deserialize(job.RequestJson);
        if (request == null)
            return new ApplyResultOutcome(false, "최적화 요청 정보를 읽지 못했습니다.", null, job.AppliedResultCount);

        OptimizationResult? candidate;
        if (resultId.HasValue)
        {
            candidate = await db.OptimizationResults
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.JobId == job.Id && r.Id == resultId.Value, ct);
        }
        else
        {
            var results = await repo.GetResultsAsync(job.Id, Math.Max(job.TopResultsToKeep, 50));
            candidate = SelectPromotionCandidate(results, job.RankBy, job.AutoApplyMinTrades);
        }

        if (candidate == null)
        {
            var message = resultId.HasValue
                ? "선택한 최적화 결과를 찾을 수 없습니다."
                : "자동 반영 후보가 없습니다. 최소 거래 수 또는 수익률 조건을 만족하지 못했습니다.";
            await SaveApplyStatusAsync(
                repo,
                job.Id,
                null,
                message);
            return new ApplyResultOutcome(false, message, null, job.AppliedResultCount);
        }

        var snapshot = JsonSerializer.Deserialize<OptimizeParamSnapshot>(candidate.ParamsJson, JsonOpts);
        if (snapshot == null)
        {
            const string message = "반영 실패: 결과 파라미터 역직렬화에 실패했습니다.";
            await SaveApplyStatusAsync(repo, job.Id, null, message);
            return new ApplyResultOutcome(false, message, null, job.AppliedResultCount);
        }

        var targetPattern = await ResolveTargetPatternAsync(patternManagement, request.BasePattern, ct);
        if (targetPattern == null)
        {
            const string message = "반영 실패: 저장된 커스텀 패턴을 찾을 수 없습니다.";
            await SaveApplyStatusAsync(repo, job.Id, null, message);
            return new ApplyResultOutcome(false, message, null, job.AppliedResultCount);
        }

        var promoted = StrategyVariantFactory.CloneStrategyDocument(targetPattern.Document);
        StrategyVariantFactory.ApplyOptimizeOverrides(promoted, snapshot);
        var promotion = await patternManagement.UpdateAsync(targetPattern.Id, promoted, ct);
        if (promotion.Kind != CustomPatternOperationKind.Success)
        {
            var message = $"반영 실패: {promotion.Error ?? "전략 검증 또는 저장에 실패했습니다."}";
            await SaveApplyStatusAsync(repo, job.Id, null, message);
            return new ApplyResultOutcome(false, message, null, job.AppliedResultCount);
        }
        targetPattern = promotion.Strategy!;

        var metricSource = candidate.OosTotalReturn.HasValue ? "OOS" : "IS";
        var metricValue = candidate.OosTotalReturn ?? candidate.TotalReturn;
        var applyLabel = isAutoApply ? "자동 반영" : "수동 반영";
        var appliedCount = await SaveApplyStatusAsync(
            repo,
            job.Id,
            candidate.Id,
            $"{targetPattern.Document.Name}에 {applyLabel} 완료 ({metricSource} return {metricValue:F2}%, trades {GetTradeCount(candidate, candidate.OosTotalReturn.HasValue)}).",
            incrementAppliedCount: true);

        _logger.LogInformation(
            "Optimization job {JobId}: {ApplyMode} result {ResultId} to custom pattern {PatternId} ({PatternName})",
            job.Id, isAutoApply ? "auto-applied" : "manually applied", candidate.Id, targetPattern.Id, targetPattern.Document.Name);

        return new ApplyResultOutcome(
            true,
            $"{targetPattern.Document.Name}에 {(isAutoApply ? "자동" : "수동")} 반영했습니다.",
            candidate.Id,
            appliedCount);
    }

    private async Task RequeueContinuousJobAsync(
        OptimizationJob completedJob,
        OptimizeRequest request,
        IOptimizationRepository repo,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var patternManagement = scope.ServiceProvider.GetRequiredService<CustomPatternManagementService>();

        var latestPattern = (await ResolveTargetPatternAsync(patternManagement, request.BasePattern, ct))?.Document
            ?? request.BasePattern;
        var nextRequest = BuildNextRequest(request, latestPattern, _clock.GetUtcNow().UtcDateTime);
        await repo.RequeueContinuousJobAsync(
            completedJob.Id,
            OptimizeRequestJsonCodec.Serialize(nextRequest));

        _logger.LogInformation(
            "Optimization job {JobId}: recycled for next continuous cycle",
            completedJob.Id);
    }

    private async Task<int> SaveApplyStatusAsync(
        IOptimizationRepository repo,
        int jobId,
        int? resultId,
        string message,
        bool incrementAppliedCount = false)
    {
        var job = await repo.GetJobSummaryAsync(jobId);
        if (job == null)
            return 0;

        job.LastAutoAppliedAt = _clock.GetUtcNow().UtcDateTime;
        job.LastAutoAppliedResultId = resultId;
        job.LastAutoApplyMessage = message;
        if (incrementAppliedCount)
            job.AppliedResultCount += 1;
        await repo.UpdateJobAsync(job);
        return job.AppliedResultCount;
    }

    private static async Task<StoredStrategy?> ResolveTargetPatternAsync(
        CustomPatternManagementService management,
        StrategyDocument basePattern,
        CancellationToken ct)
    {
        if (basePattern.StoredStrategyId is > 0)
            return await management.FindAsync(basePattern.StoredStrategyId.Value, ct);

        if (!string.IsNullOrWhiteSpace(basePattern.Name))
            return await management.FindByNameAsync(basePattern.Name, ct);

        return null;
    }

    private static OptimizeRequest CloneOptimizeRequest(OptimizeRequest src) =>
        OptimizeRequestJsonCodec.Clone(src);

    private static int GetTradeCount(OptimizationResult result, bool useOos)
        => useOos ? result.OosTotalTrades ?? 0 : result.TotalTrades;

    private static decimal GetTotalReturn(OptimizationResult result, bool useOos)
        => useOos ? result.OosTotalReturn ?? decimal.MinValue : result.TotalReturn;

    private static IEnumerable<OptimizationResult> SortResults(
        IEnumerable<OptimizationResult> results,
        string rankBy,
        bool useOos)
    {
        return rankBy.ToLowerInvariant() switch
        {
            "totalreturn" => results.OrderByDescending(r => useOos ? r.OosTotalReturn ?? decimal.MinValue : r.TotalReturn),
            "sharperatio" => results.OrderByDescending(r => useOos ? r.OosSharpeRatio ?? decimal.MinValue : r.SharpeRatio),
            "calmarratio" => results.OrderByDescending(r => useOos ? r.OosCalmarRatio ?? decimal.MinValue : r.CalmarRatio),
            "profitfactor" => results.OrderByDescending(r => useOos ? r.OosProfitFactor ?? decimal.MinValue : r.ProfitFactor),
            "winrate" => results.OrderByDescending(r => useOos ? r.OosWinRate ?? decimal.MinValue : r.WinRate),
            "annualizedreturn" => results.OrderByDescending(r => useOos ? r.OosAnnualizedReturn ?? decimal.MinValue : r.AnnualizedReturn),
            _ => results.OrderByDescending(r => useOos ? r.OosSortinoRatio ?? decimal.MinValue : r.SortinoRatio)
        };
    }
}
