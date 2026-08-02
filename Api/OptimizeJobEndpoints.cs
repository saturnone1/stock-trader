using System.Text.Json;
using StockTrader.BackgroundServices;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Api;

/// <summary>
/// 연속 파라미터 최적화 Job 관련 Minimal API 엔드포인트.
/// Job의 생성, 조회, 제어(취소/일시정지/재개), 삭제, 결과 조회를 담당합니다.
/// </summary>
public static class OptimizeJobEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static RouteGroupBuilder MapOptimizeJobApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/optimize-jobs").RequireAuthorization();

        // ── POST /api/optimize-jobs — Job 생성 ─────────────────────────────────
        group.MapPost("/", async (
            CreateOptimizeJobRequest req,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Job 이름을 입력하세요." });

            var requestJson = JsonSerializer.Serialize(req.OptimizeRequest, _jsonOptions);
            var totalCombinations = CalculateTotalCombinations(req.OptimizeRequest.OptimizeParams);

            var job = new OptimizationJob
            {
                Name                  = req.Name.Trim(),
                Priority              = req.Priority,
                ChunkSize             = req.ChunkSize > 0 ? req.ChunkSize : 200,
                MaxDurationHours      = req.MaxDurationHours,
                MaxTestedCombinations = req.MaxTestedCombinations,
                TopResultsToKeep      = req.TopResultsToKeep > 0 ? req.TopResultsToKeep : 50,
                RankBy                = string.IsNullOrWhiteSpace(req.RankBy) ? "sortinoRatio" : req.RankBy,
                ContinuousMode        = req.ContinuousMode,
                AutoApplyBestResult   = req.AutoApplyBestResult,
                AutoApplyMinTrades    = req.AutoApplyMinTrades > 0 ? req.AutoApplyMinTrades : 10,
                RequestJson           = requestJson,
                TotalCombinations     = totalCombinations,
                Status                = OptimizationJobStatus.Pending,
                CreatedAt             = DateTime.UtcNow,
            };

            var created = await repo.CreateJobAsync(job);
            return Results.Created($"/api/optimize-jobs/{created.Id}", ToSummary(created));
        });

        // ── GET /api/optimize-jobs — Job 목록 ──────────────────────────────────
        group.MapGet("/", async (
            string? status,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            OptimizationJobStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<OptimizationJobStatus>(status, ignoreCase: true, out var parsed))
            {
                statusFilter = parsed;
            }

            var jobs = await repo.GetJobsAsync(statusFilter);
            return Results.Ok(jobs.Select(ToSummary).ToList());
        });

        // ── GET /api/optimize-jobs/{id} — Job 상세 ─────────────────────────────
        group.MapGet("/{id:int}", async (
            int id,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            return Results.Ok(ToDetail(job));
        });

        group.MapPost("/{id:int}/settings", async (
            int id,
            UpdateOptimizeJobSettingsRequest req,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            if (req.AutoApplyBestResult.HasValue)
                job.AutoApplyBestResult = req.AutoApplyBestResult.Value;

            if (req.AutoApplyMinTrades.HasValue && req.AutoApplyMinTrades.Value > 0)
                job.AutoApplyMinTrades = req.AutoApplyMinTrades.Value;

            await repo.UpdateJobAsync(job);
            return Results.Ok(ToSummary(job));
        });

        group.MapPost("/{id:int}/apply-result", async (
            int id,
            ApplyOptimizeJobResultRequest req,
            OptimizationAutoTuneService autoTuneService,
            CancellationToken ct) =>
        {
            var outcome = await autoTuneService.ApplyResultAsync(id, req.ResultId, isAutoApply: false, ct);
            if (!outcome.Success)
                return Results.BadRequest(new ApplyOptimizeJobResultResponse
                {
                    Success = false,
                    Message = outcome.Message,
                    AppliedResultId = outcome.AppliedResultId,
                    AppliedResultCount = outcome.AppliedResultCount
                });

            return Results.Ok(new ApplyOptimizeJobResultResponse
            {
                Success = true,
                Message = outcome.Message,
                AppliedResultId = outcome.AppliedResultId,
                AppliedResultCount = outcome.AppliedResultCount
            });
        });

        // ── POST /api/optimize-jobs/{id}/cancel — 취소 ─────────────────────────
        group.MapPost("/{id:int}/cancel", async (
            int id,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            if (job.Status is OptimizationJobStatus.Completed
                           or OptimizationJobStatus.Cancelled
                           or OptimizationJobStatus.Failed)
                return Results.BadRequest(new { error = $"이미 종료된 Job입니다. 현재 상태: {job.Status}" });

            job.Status      = OptimizationJobStatus.Cancelled;
            job.CompletedAt = DateTime.UtcNow;
            await repo.UpdateJobAsync(job);

            return Results.Ok(ToSummary(job));
        });

        // ── POST /api/optimize-jobs/{id}/pause — 일시정지 ──────────────────────
        group.MapPost("/{id:int}/pause", async (
            int id,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            if (job.Status is not (OptimizationJobStatus.Pending or OptimizationJobStatus.Running))
                return Results.BadRequest(new { error = $"Pending 또는 Running 상태일 때만 일시정지할 수 있습니다. 현재 상태: {job.Status}" });

            job.Status = OptimizationJobStatus.Paused;
            await repo.UpdateJobAsync(job);

            return Results.Ok(ToSummary(job));
        });

        // ── POST /api/optimize-jobs/{id}/resume — 재개 ─────────────────────────
        group.MapPost("/{id:int}/resume", async (
            int id,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            if (job.Status is not OptimizationJobStatus.Paused)
                return Results.BadRequest(new { error = $"Paused 상태일 때만 재개할 수 있습니다. 현재 상태: {job.Status}" });

            job.Status = OptimizationJobStatus.Pending;
            await repo.UpdateJobAsync(job);

            return Results.Ok(ToSummary(job));
        });

        // ── DELETE /api/optimize-jobs/{id} — 삭제 ──────────────────────────────
        group.MapDelete("/{id:int}", async (
            int id,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            if (job.Status is not (OptimizationJobStatus.Completed
                                or OptimizationJobStatus.Cancelled
                                or OptimizationJobStatus.Failed))
                return Results.BadRequest(new { error = "Completed, Cancelled, Failed 상태인 Job만 삭제할 수 있습니다." });

            await repo.DeleteJobAsync(id);
            return Results.Ok();
        });

        // ── GET /api/optimize-jobs/{id}/results — 결과 조회 ────────────────────
        group.MapGet("/{id:int}/results", async (
            int id,
            int? top,
            IOptimizationRepository repo,
            CancellationToken ct) =>
        {
            var job = await repo.GetJobAsync(id);
            if (job is null)
                return Results.NotFound();

            var topCount = top is > 0 ? top.Value : 50;
            var results  = await repo.GetResultsAsync(id, topCount);

            var items = results.Select(r => ToResultItem(r)).ToList();
            return Results.Ok(items);
        });

        return api;
    }

    // ── 매핑 헬퍼 ─────────────────────────────────────────────────────────────

    private static OptimizeJobSummary ToSummary(OptimizationJob job) => new()
    {
        Id                  = job.Id,
        Name                = job.Name,
        Status              = job.Status.ToString(),
        TotalCombinations   = job.TotalCombinations,
        TestedCombinations  = job.TestedCombinations,
        ProgressPercent     = CalculateProgress(job),
        CreatedAt           = job.CreatedAt,
        StartedAt           = job.StartedAt,
        ContinuousMode      = job.ContinuousMode,
        AutoApplyBestResult = job.AutoApplyBestResult,
        AutoApplyMinTrades  = job.AutoApplyMinTrades,
        AppliedResultCount  = job.AppliedResultCount,
        LastAutoAppliedAt   = job.LastAutoAppliedAt,
        LastAutoAppliedResultId = job.LastAutoAppliedResultId,
        LastAutoApplyMessage = job.LastAutoApplyMessage,
    };

    private static OptimizeJobDetail ToDetail(OptimizationJob job)
    {
        var elapsed   = CalculateElapsedSeconds(job);
        var remaining = CalculateRemainingSeconds(job, elapsed);

        // 상위 3개 미리보기
        var topPreview = job.Results.Count > 0
            ? job.Results
                .OrderBy(r => r.Rank)
                .Take(3)
                .Select(r => ToResultItem(r))
                .ToList()
            : null;

        return new OptimizeJobDetail
        {
            Id                         = job.Id,
            Name                       = job.Name,
            Status                     = job.Status.ToString(),
            TotalCombinations          = job.TotalCombinations,
            TestedCombinations         = job.TestedCombinations,
            ProgressPercent            = CalculateProgress(job),
            ElapsedSeconds             = elapsed,
            EstimatedRemainingSeconds  = remaining,
            CreatedAt                  = job.CreatedAt,
            StartedAt                  = job.StartedAt,
            CompletedAt                = job.CompletedAt,
            LastProgressAt             = job.LastProgressAt,
            ErrorMessage               = job.ErrorMessage,
            ContinuousMode             = job.ContinuousMode,
            AutoApplyBestResult        = job.AutoApplyBestResult,
            AutoApplyMinTrades         = job.AutoApplyMinTrades,
            AppliedResultCount         = job.AppliedResultCount,
            LastAutoAppliedAt          = job.LastAutoAppliedAt,
            LastAutoAppliedResultId    = job.LastAutoAppliedResultId,
            LastAutoApplyMessage       = job.LastAutoApplyMessage,
            TopResults                 = topPreview,
        };
    }

    private static OptimizeResultItem ToResultItem(OptimizationResult r)
    {
        // ParamsJson → OptimizeParamSnapshot 역직렬화
        OptimizeParamSnapshot? paramsSnapshot = null;
        if (!string.IsNullOrWhiteSpace(r.ParamsJson))
        {
            try
            {
                paramsSnapshot = JsonSerializer.Deserialize<OptimizeParamSnapshot>(
                    r.ParamsJson, _jsonOptions);
            }
            catch
            {
                // 역직렬화 실패 시 빈 스냅샷 사용
                paramsSnapshot = null;
            }
        }

        return new OptimizeResultItem
        {
            Rank                 = r.Rank,
            Params               = paramsSnapshot ?? new OptimizeParamSnapshot(),
            TotalReturn          = r.TotalReturn,
            SortinoRatio         = r.SortinoRatio,
            SharpeRatio          = r.SharpeRatio,
            MaxDrawdown          = r.MaxDrawdown,
            WinRate              = r.WinRate,
            TotalTrades          = r.TotalTrades,
            ProfitFactor         = r.ProfitFactor,
            CalmarRatio          = r.CalmarRatio,
            AnnualizedReturn     = r.AnnualizedReturn,
            OosTotalReturn       = r.OosTotalReturn,
            OosSortinoRatio      = r.OosSortinoRatio,
            OosSharpeRatio       = r.OosSharpeRatio,
            OosMaxDrawdown       = r.OosMaxDrawdown,
            OosWinRate           = r.OosWinRate,
            OosTotalTrades       = r.OosTotalTrades,
            OosProfitFactor      = r.OosProfitFactor,
            OosCalmarRatio       = r.OosCalmarRatio,
            OosAnnualizedReturn  = r.OosAnnualizedReturn,
        };
    }

    // ── 계산 헬퍼 ─────────────────────────────────────────────────────────────

    private static decimal CalculateProgress(OptimizationJob job)
    {
        if (job.TotalCombinations <= 0) return 0m;
        var raw = (decimal)job.TestedCombinations / job.TotalCombinations * 100m;
        return Math.Min(Math.Round(raw, 2), 100m);
    }

    private static double? CalculateElapsedSeconds(OptimizationJob job)
    {
        if (job.StartedAt is null) return null;
        var end = job.CompletedAt ?? DateTime.UtcNow;
        return (end - job.StartedAt.Value).TotalSeconds;
    }

    private static double? CalculateRemainingSeconds(OptimizationJob job, double? elapsedSeconds)
    {
        if (elapsedSeconds is null or <= 0) return null;
        if (job.TestedCombinations <= 0) return null;
        if (job.TotalCombinations <= job.TestedCombinations) return 0;

        // elapsed / tested * remaining
        var remaining = job.TotalCombinations - job.TestedCombinations;
        var rate = elapsedSeconds.Value / job.TestedCombinations; // seconds per combination
        return rate * remaining;
    }

    /// <summary>
    /// OptimizeParams에서 총 조합 수를 계산합니다.
    /// BacktestService.GenerateOptimizeCombinations와 동일한 축 구성 로직을 사용하며,
    /// 실제 조합 객체를 생성하지 않고 크기만 계산합니다.
    /// </summary>
    private static long CalculateTotalCombinations(OptimizeParams p)
    {
        // 각 축의 크기 목록 (설정되지 않은 축은 크기 1)
        var axisSizes = new List<int>();

        // 숫자형 단순 축 헬퍼
        void AddNumericAxisSize(ParamRange? range)
        {
            var vals = range?.Enumerate().ToList();
            axisSizes.Add(vals is { Count: > 0 } ? vals.Count : 1);
        }

        // 기존 5개 숫자형 축
        AddNumericAxisSize(p.AtrStopMultiplier);
        AddNumericAxisSize(p.AtrTargetMultiplier);
        AddNumericAxisSize(p.MaxHoldingBars);
        AddNumericAxisSize(p.TrailingAtr);
        AddNumericAxisSize(p.PartialProfitR);

        // 추가 숫자형 축
        AddNumericAxisSize(p.DefaultAllocationPercent);
        AddNumericAxisSize(p.CircuitBreakerConsecutiveLossLimit);
        AddNumericAxisSize(p.CircuitBreakerCooldownBars);
        AddNumericAxisSize(p.CircuitBreakerMaxDrawdownPercent);
        AddNumericAxisSize(p.ReentryCooldownAfterLoss);
        AddNumericAxisSize(p.ReentryCooldownAfterWin);
        AddNumericAxisSize(p.PortfolioMaxPositions);
        AddNumericAxisSize(p.PortfolioMaxSinglePercent);
        AddNumericAxisSize(p.PortfolioMaxEntriesPerDay);

        // 카테고리형 축
        axisSizes.Add(p.EntryLogicOptions is { Count: > 0 }        ? p.EntryLogicOptions.Count        : 1);
        axisSizes.Add(p.RequireBullRegimeOptions is { Count: > 0 }  ? p.RequireBullRegimeOptions.Count  : 1);
        axisSizes.Add(p.EntryModeOptions is { Count: > 0 }          ? p.EntryModeOptions.Count          : 1);
        axisSizes.Add(p.SizingModeOptions is { Count: > 0 }         ? p.SizingModeOptions.Count         : 1);
        axisSizes.Add(p.ExitLogicOptions is { Count: > 0 }          ? p.ExitLogicOptions.Count          : 1);
        axisSizes.Add(p.TimeFrameOptions is { Count: > 0 }          ? p.TimeFrameOptions.Count          : 1);

        // 룰 파라미터 오버라이드 축
        foreach (var dim in p.RuleParamOverrides ?? [])
        {
            if (dim.Values.Count > 0)
                axisSizes.Add(dim.Values.Count);
        }

        // 룰 필드 오버라이드 축
        foreach (var dim in p.RuleFieldOverrides ?? [])
        {
            var count = (dim.NumericValues?.Count ?? 0) + (dim.StringValues?.Count ?? 0);
            if (count > 0)
                axisSizes.Add(count);
        }

        // 총 조합 수 = 각 축 크기의 곱 (오버플로우 방지)
        long total = 1;
        foreach (var size in axisSizes)
        {
            total *= size;
            if (total > 1_000_000_000L) // 10억 초과 시 cap
                return 1_000_000_000L;
        }

        return total;
    }
}
