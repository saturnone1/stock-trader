using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;

namespace StockTrader.Application.Optimization;

public sealed record OptimizationAutoTuneJob(
    int Id,
    OptimizeRequest? Request,
    bool AutoApplyBestResult,
    bool ContinuousMode,
    string RankBy,
    int TopResultsToKeep,
    int AutoApplyMinTrades,
    int AppliedResultCount);

public sealed record OptimizationPromotionCandidate(
    int Id,
    OptimizeParamSnapshot? Parameters,
    decimal TotalReturn,
    decimal SortinoRatio,
    decimal SharpeRatio,
    decimal WinRate,
    int TotalTrades,
    decimal ProfitFactor,
    decimal CalmarRatio,
    decimal AnnualizedReturn,
    decimal? OosTotalReturn,
    decimal? OosSortinoRatio,
    decimal? OosSharpeRatio,
    decimal? OosWinRate,
    int? OosTotalTrades,
    decimal? OosProfitFactor,
    decimal? OosCalmarRatio,
    decimal? OosAnnualizedReturn);

public interface IOptimizationAutoTuneStore
{
    Task<OptimizationAutoTuneJob?> FindJobAsync(int id, CancellationToken ct = default);
    Task<OptimizationPromotionCandidate?> FindCandidateAsync(
        int jobId, int resultId, CancellationToken ct = default);
    Task<IReadOnlyList<OptimizationPromotionCandidate>> ListCandidatesAsync(
        int jobId, int count, CancellationToken ct = default);
    Task<int> RecordApplyOutcomeAsync(
        int jobId,
        int? resultId,
        string message,
        DateTime observedAt,
        bool incrementAppliedCount,
        CancellationToken ct = default);
    Task<bool> RequeueAsync(
        int jobId, OptimizeRequest nextRequest, CancellationToken ct = default);
}

public static class OptimizationPromotionPolicy
{
    public static OptimizationPromotionCandidate? SelectCandidate(
        IReadOnlyCollection<OptimizationPromotionCandidate> results,
        string rankBy,
        int minTrades)
    {
        if (results.Count == 0) return null;

        var useOos = results.Any(result => result.OosTotalReturn.HasValue);
        var eligible = results
            .Where(result => TradeCount(result, useOos) >= minTrades)
            .Where(result => TotalReturn(result, useOos) > 0);

        return Sort(eligible, rankBy, useOos).FirstOrDefault();
    }

    public static OptimizeRequest BuildNextRequest(
        OptimizeRequest currentRequest,
        StrategyDocument basePattern,
        DateTime utcNow)
    {
        var next = OptimizeRequestJsonCodec.Clone(currentRequest);
        next.BasePattern = StrategyVariantFactory.CloneStrategyDocument(basePattern);

        var span = currentRequest.To - currentRequest.From;
        if (span <= TimeSpan.Zero) span = TimeSpan.FromDays(365);

        var candidateTo = utcNow.Date > currentRequest.To ? utcNow.Date : currentRequest.To;
        next.To = candidateTo;
        next.From = candidateTo - span;
        return next;
    }

    public static int TradeCount(OptimizationPromotionCandidate result, bool useOos) =>
        useOos ? result.OosTotalTrades ?? 0 : result.TotalTrades;

    private static decimal TotalReturn(OptimizationPromotionCandidate result, bool useOos) =>
        useOos ? result.OosTotalReturn ?? decimal.MinValue : result.TotalReturn;

    private static IOrderedEnumerable<OptimizationPromotionCandidate> Sort(
        IEnumerable<OptimizationPromotionCandidate> results,
        string rankBy,
        bool useOos) => rankBy.ToLowerInvariant() switch
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

public sealed class OptimizationAutoTuneService
{
    public sealed record ApplyResultOutcome(
        bool Success, string Message, int? AppliedResultId, int AppliedResultCount);

    private readonly IOptimizationAutoTuneStore _store;
    private readonly CustomPatternManagementService _patterns;
    private readonly ILogger<OptimizationAutoTuneService> _logger;
    private readonly TimeProvider _clock;

    public OptimizationAutoTuneService(
        IOptimizationAutoTuneStore store,
        CustomPatternManagementService patterns,
        ILogger<OptimizationAutoTuneService> logger,
        TimeProvider clock)
    {
        _store = store;
        _patterns = patterns;
        _logger = logger;
        _clock = clock;
    }

    public async Task HandleCompletedJobAsync(int jobId, CancellationToken ct = default)
    {
        var job = await _store.FindJobAsync(jobId, ct);
        if (job?.Request is null) return;

        if (job.AutoApplyBestResult)
            await ApplyResultAsync(job.Id, isAutoApply: true, ct: ct);

        if (!job.ContinuousMode) return;

        var latest = (await ResolveTargetPatternAsync(job.Request.BasePattern, ct))?.Document
            ?? job.Request.BasePattern;
        var next = OptimizationPromotionPolicy.BuildNextRequest(
            job.Request, latest, _clock.GetUtcNow().UtcDateTime);
        if (await _store.RequeueAsync(job.Id, next, ct))
            _logger.LogInformation(
                "Optimization job {JobId}: recycled for next continuous cycle", job.Id);
    }

    public async Task<ApplyResultOutcome> ApplyResultAsync(
        int jobId,
        int? resultId = null,
        bool isAutoApply = false,
        CancellationToken ct = default)
    {
        var job = await _store.FindJobAsync(jobId, ct);
        if (job is null)
            return new(false, "최적화 Job을 찾을 수 없습니다.", null, 0);
        if (job.Request is null)
            return new(false, "최적화 요청 정보를 읽지 못했습니다.", null, job.AppliedResultCount);

        var candidate = resultId.HasValue
            ? await _store.FindCandidateAsync(job.Id, resultId.Value, ct)
            : OptimizationPromotionPolicy.SelectCandidate(
                await _store.ListCandidatesAsync(job.Id, Math.Max(job.TopResultsToKeep, 50), ct),
                job.RankBy,
                job.AutoApplyMinTrades);

        if (candidate is null)
        {
            var message = resultId.HasValue
                ? "선택한 최적화 결과를 찾을 수 없습니다."
                : "자동 반영 후보가 없습니다. 최소 거래 수 또는 수익률 조건을 만족하지 못했습니다.";
            var unchangedCount = await RecordOutcomeAsync(job.Id, null, message, false, ct);
            return new(false, message, null, unchangedCount);
        }

        if (candidate.Parameters is null)
            return await FailureAsync(job.Id, "반영 실패: 결과 파라미터 역직렬화에 실패했습니다.", ct);

        var target = await ResolveTargetPatternAsync(job.Request.BasePattern, ct);
        if (target is null)
            return await FailureAsync(job.Id, "반영 실패: 저장된 커스텀 패턴을 찾을 수 없습니다.", ct);

        var promoted = StrategyVariantFactory.CloneStrategyDocument(target.Document);
        StrategyVariantFactory.ApplyOptimizeOverrides(promoted, candidate.Parameters);
        var promotion = await _patterns.UpdateAsync(target.Id, promoted, ct);
        if (promotion.Kind != CustomPatternOperationKind.Success)
            return await FailureAsync(
                job.Id,
                $"반영 실패: {promotion.Error ?? "전략 검증 또는 저장에 실패했습니다."}",
                ct);

        target = promotion.Strategy!;
        var useOos = candidate.OosTotalReturn.HasValue;
        var metricValue = candidate.OosTotalReturn ?? candidate.TotalReturn;
        var label = isAutoApply ? "자동 반영" : "수동 반영";
        var count = await RecordOutcomeAsync(
            job.Id,
            candidate.Id,
            $"{target.Document.Name}에 {label} 완료 ({(useOos ? "OOS" : "IS")} return {metricValue:F2}%, trades {OptimizationPromotionPolicy.TradeCount(candidate, useOos)}).",
            true,
            ct);

        _logger.LogInformation(
            "Optimization job {JobId}: {ApplyMode} result {ResultId} to custom pattern {PatternId} ({PatternName})",
            job.Id, isAutoApply ? "auto-applied" : "manually applied", candidate.Id,
            target.Id, target.Document.Name);
        return new(true, $"{target.Document.Name}에 {(isAutoApply ? "자동" : "수동")} 반영했습니다.", candidate.Id, count);
    }

    private async Task<ApplyResultOutcome> FailureAsync(
        int jobId, string message, CancellationToken ct)
    {
        var count = await RecordOutcomeAsync(jobId, null, message, false, ct);
        return new(false, message, null, count);
    }

    private Task<int> RecordOutcomeAsync(
        int jobId, int? resultId, string message, bool increment, CancellationToken ct) =>
        _store.RecordApplyOutcomeAsync(
            jobId, resultId, message, _clock.GetUtcNow().UtcDateTime, increment, ct);

    private async Task<StoredStrategy?> ResolveTargetPatternAsync(
        StrategyDocument basePattern, CancellationToken ct)
    {
        if (basePattern.StoredStrategyId is > 0)
            return await _patterns.FindAsync(basePattern.StoredStrategyId.Value, ct);
        return string.IsNullOrWhiteSpace(basePattern.Name)
            ? null
            : await _patterns.FindByNameAsync(basePattern.Name, ct);
    }
}
