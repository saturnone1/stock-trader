using StockTrader.Application.Optimization;

namespace StockTrader.Api;

/// <summary>최적화 작업 HTTP 계약을 애플리케이션 사용 사례에 연결합니다.</summary>
public static class OptimizeJobEndpoints
{
    public static RouteGroupBuilder MapOptimizeJobApi(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/optimize-jobs").RequireAuthorization();

        group.MapPost("/", async (
            CreateOptimizeJobRequest request,
            OptimizationJobManagementService jobs,
            CancellationToken cancellationToken) =>
        {
            var result = await jobs.CreateAsync(
                new CreateOptimizationJobCommand(
                    request.Name,
                    request.Priority,
                    request.ChunkSize,
                    request.MaxDurationHours,
                    request.MaxTestedCombinations,
                    request.TopResultsToKeep,
                    request.RankBy,
                    request.ContinuousMode,
                    request.AutoApplyBestResult,
                    request.AutoApplyMinTrades,
                    request.OptimizeRequest),
                cancellationToken);
            if (result.Outcome == OptimizationJobCreateOutcome.InvalidName)
                return Results.BadRequest(new { error = "Job 이름을 입력하세요." });
            if (result.Outcome == OptimizationJobCreateOutcome.UnsupportedRemoteDuration)
                return Results.BadRequest(new
                {
                    error = "원격 최적화에서는 시간 제한 대신 최대 테스트 조합 수를 사용하세요."
                });

            var created = OptimizationJobApiMapper.ToSummary(result.Job!);
            return Results.Created($"/api/optimize-jobs/{created.Id}", created);
        });

        group.MapGet("/", async (
            string? status,
            OptimizationJobManagementService jobs,
            CancellationToken cancellationToken) =>
        {
            var result = await jobs.ListAsync(status, cancellationToken);
            return Results.Ok(result.Select(OptimizationJobApiMapper.ToSummary).ToList());
        });

        group.MapGet("/{id:int}", async (
            int id,
            OptimizationJobManagementService jobs,
            CancellationToken cancellationToken) =>
        {
            var job = await jobs.FindAsync(id, cancellationToken);
            return job is null
                ? Results.NotFound()
                : Results.Ok(OptimizationJobApiMapper.ToDetail(job));
        });

        group.MapPost("/{id:int}/settings", async (
            int id,
            UpdateOptimizeJobSettingsRequest request,
            OptimizationJobManagementService jobs,
            CancellationToken cancellationToken) =>
        {
            var job = await jobs.UpdateSettingsAsync(
                id,
                new UpdateOptimizationJobSettingsCommand(
                    request.AutoApplyBestResult,
                    request.AutoApplyMinTrades),
                cancellationToken);
            return job is null
                ? Results.NotFound()
                : Results.Ok(OptimizationJobApiMapper.ToSummary(job));
        });

        group.MapPost("/{id:int}/apply-result", async (
            int id,
            ApplyOptimizeJobResultRequest request,
            OptimizationAutoTuneService autoTuneService,
            CancellationToken cancellationToken) =>
        {
            var outcome = await autoTuneService.ApplyResultAsync(
                id, request.ResultId, isAutoApply: false, cancellationToken);
            var response = new ApplyOptimizeJobResultResponse
            {
                Success = outcome.Success,
                Message = outcome.Message,
                AppliedResultId = outcome.AppliedResultId,
                AppliedResultCount = outcome.AppliedResultCount
            };
            return outcome.Success ? Results.Ok(response) : Results.BadRequest(response);
        });

        group.MapPost("/{id:int}/cancel", async (
            int id,
            OptimizationJobControlService controls,
            OptimizationJobManagementService jobs,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
            await ApplyControlAsync(
                id,
                OptimizationJobControlCommand.Cancel,
                controls,
                jobs,
                clock.GetUtcNow().UtcDateTime,
                cancellationToken));

        group.MapPost("/{id:int}/pause", async (
            int id,
            OptimizationJobControlService controls,
            OptimizationJobManagementService jobs,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
            await ApplyControlAsync(
                id,
                OptimizationJobControlCommand.Pause,
                controls,
                jobs,
                clock.GetUtcNow().UtcDateTime,
                cancellationToken));

        group.MapPost("/{id:int}/resume", async (
            int id,
            OptimizationJobControlService controls,
            OptimizationJobManagementService jobs,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
            await ApplyControlAsync(
                id,
                OptimizationJobControlCommand.Resume,
                controls,
                jobs,
                clock.GetUtcNow().UtcDateTime,
                cancellationToken));

        group.MapDelete("/{id:int}", async (
            int id,
            OptimizationJobManagementService jobs,
            CancellationToken cancellationToken) =>
        {
            var result = await jobs.DeleteAsync(id, cancellationToken);
            return result.Outcome switch
            {
                OptimizationJobDeleteOutcome.Deleted => Results.Ok(),
                OptimizationJobDeleteOutcome.NotFound => Results.NotFound(),
                OptimizationJobDeleteOutcome.InvalidState => Results.BadRequest(new
                {
                    error = "Completed, Cancelled, Failed 상태인 Job만 삭제할 수 있습니다."
                }),
                OptimizationJobDeleteOutcome.ConcurrentChange => Results.Conflict(new
                {
                    error = $"작업 상태가 동시에 변경되었습니다. 현재 상태: {result.State}"
                }),
                _ => throw new ArgumentOutOfRangeException()
            };
        });

        group.MapGet("/{id:int}/results", async (
            int id,
            int? top,
            OptimizationJobManagementService jobs,
            CancellationToken cancellationToken) =>
        {
            var results = await jobs.GetResultsAsync(id, top, cancellationToken);
            return results is null ? Results.NotFound() : Results.Ok(results);
        });

        return api;
    }

    private static async Task<IResult> ApplyControlAsync(
        int jobId,
        OptimizationJobControlCommand command,
        OptimizationJobControlService controls,
        OptimizationJobManagementService jobs,
        DateTime observedAt,
        CancellationToken cancellationToken)
    {
        var result = await controls.ApplyAsync(
            jobId, command, observedAt, cancellationToken);
        if (result.Outcome == OptimizationJobControlOutcome.NotFound)
            return Results.NotFound();
        if (result.Outcome == OptimizationJobControlOutcome.ConcurrentChange)
            return Results.Conflict(new
            {
                error = $"작업 상태가 동시에 변경되었습니다. 현재 상태: {result.State}"
            });
        if (result.Outcome == OptimizationJobControlOutcome.InvalidState)
            return Results.BadRequest(new
            {
                error = InvalidControlMessage(command, result.State!.Value)
            });

        var job = await jobs.FindSummaryAsync(jobId, cancellationToken);
        return job is null
            ? Results.NotFound()
            : Results.Ok(OptimizationJobApiMapper.ToSummary(job));
    }

    private static string InvalidControlMessage(
        OptimizationJobControlCommand command,
        OptimizationJobControlState state) => command switch
    {
        OptimizationJobControlCommand.Cancel =>
            $"이미 종료된 Job입니다. 현재 상태: {state}",
        OptimizationJobControlCommand.Pause =>
            $"Pending 또는 Running 상태일 때만 일시정지할 수 있습니다. 현재 상태: {state}",
        OptimizationJobControlCommand.Resume =>
            $"Paused 상태일 때만 재개할 수 있습니다. 현재 상태: {state}",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null)
    };
}
