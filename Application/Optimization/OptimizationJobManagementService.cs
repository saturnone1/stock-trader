namespace StockTrader.Application.Optimization;

public sealed record CreateOptimizationJobCommand(
    string Name,
    int Priority,
    int ChunkSize,
    decimal? MaxDurationHours,
    long? MaxTestedCombinations,
    int TopResultsToKeep,
    string RankBy,
    bool ContinuousMode,
    bool AutoApplyBestResult,
    int AutoApplyMinTrades,
    OptimizeRequest Request);

public sealed record UpdateOptimizationJobSettingsCommand(
    bool? AutoApplyBestResult,
    int? AutoApplyMinTrades);

public sealed record OptimizationJobSummaryView(
    int Id,
    string Name,
    OptimizationJobControlState State,
    long TotalCombinations,
    long TestedCombinations,
    decimal ProgressPercent,
    DateTime CreatedAt,
    DateTime? StartedAt,
    bool ContinuousMode,
    bool AutoApplyBestResult,
    int AutoApplyMinTrades,
    int AppliedResultCount,
    DateTime? LastAutoAppliedAt,
    int? LastAutoAppliedResultId,
    string? LastAutoApplyMessage);

public sealed record OptimizationJobDetailView(
    OptimizationJobSummaryView Summary,
    double? ElapsedSeconds,
    double? EstimatedRemainingSeconds,
    DateTime? CompletedAt,
    DateTime? LastProgressAt,
    string? ErrorMessage,
    IReadOnlyList<OptimizeResultItem> TopResults);

public sealed record OptimizationJobRecord
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public OptimizationJobControlState State { get; init; }
    public int Priority { get; init; }
    public string RequestJson { get; init; } = string.Empty;
    public long TotalCombinations { get; init; }
    public long TestedCombinations { get; init; }
    public int CurrentChunkIndex { get; init; }
    public int ChunkSize { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? LastProgressAt { get; init; }
    public decimal? MaxDurationHours { get; init; }
    public long? MaxTestedCombinations { get; init; }
    public string RankBy { get; init; } = "sortinoRatio";
    public int TopResultsToKeep { get; init; }
    public bool ContinuousMode { get; init; }
    public bool AutoApplyBestResult { get; init; }
    public int AutoApplyMinTrades { get; init; }
    public int AppliedResultCount { get; init; }
    public DateTime? LastAutoAppliedAt { get; init; }
    public int? LastAutoAppliedResultId { get; init; }
    public string? LastAutoApplyMessage { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<OptimizeResultItem> Results { get; init; } = [];
}

public interface IOptimizationJobManagementStore
{
    Task<OptimizationJobRecord> CreateAsync(
        OptimizationJobRecord job,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OptimizationJobRecord>> ListAsync(
        OptimizationJobControlState? state,
        CancellationToken cancellationToken = default);

    Task<OptimizationJobRecord?> FindAsync(
        int jobId,
        int topResults,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateSettingsAsync(
        int jobId,
        bool? autoApplyBestResult,
        int? autoApplyMinTrades,
        CancellationToken cancellationToken = default);

    Task<bool> TryDeleteAsync(
        int jobId,
        OptimizationJobControlState expectedState,
        CancellationToken cancellationToken = default);
}

public enum OptimizationJobCreateOutcome
{
    Created,
    InvalidName
}

public sealed record OptimizationJobCreateResult(
    OptimizationJobCreateOutcome Outcome,
    OptimizationJobSummaryView? Job = null);

public enum OptimizationJobDeleteOutcome
{
    Deleted,
    NotFound,
    InvalidState,
    ConcurrentChange
}

public sealed record OptimizationJobDeleteResult(
    OptimizationJobDeleteOutcome Outcome,
    OptimizationJobControlState? State = null);

/// <summary>최적화 작업 생성, 조회, 설정, 삭제를 조정하는 애플리케이션 사용 사례입니다.</summary>
public sealed class OptimizationJobManagementService
{
    private const int DefaultChunkSize = 200;
    private const int DefaultTopResults = 50;
    private const int DefaultAutoApplyMinTrades = 10;

    private readonly IOptimizationJobManagementStore _store;
    private readonly TimeProvider _clock;

    public OptimizationJobManagementService(
        IOptimizationJobManagementStore store,
        TimeProvider clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<OptimizationJobCreateResult> CreateAsync(
        CreateOptimizationJobCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            return new OptimizationJobCreateResult(OptimizationJobCreateOutcome.InvalidName);

        var stored = await _store.CreateAsync(new OptimizationJobRecord
        {
            Name = command.Name.Trim(),
            State = OptimizationJobControlState.Pending,
            Priority = command.Priority,
            RequestJson = OptimizeRequestJsonCodec.Serialize(command.Request),
            TotalCombinations = OptimizationCombinationCountPolicy.Calculate(
                command.Request.OptimizeParams),
            ChunkSize = command.ChunkSize > 0 ? command.ChunkSize : DefaultChunkSize,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
            MaxDurationHours = command.MaxDurationHours,
            MaxTestedCombinations = command.MaxTestedCombinations,
            RankBy = string.IsNullOrWhiteSpace(command.RankBy)
                ? "sortinoRatio"
                : command.RankBy,
            TopResultsToKeep = command.TopResultsToKeep > 0
                ? command.TopResultsToKeep
                : DefaultTopResults,
            ContinuousMode = command.ContinuousMode,
            AutoApplyBestResult = command.AutoApplyBestResult,
            AutoApplyMinTrades = command.AutoApplyMinTrades > 0
                ? command.AutoApplyMinTrades
                : DefaultAutoApplyMinTrades
        }, cancellationToken);

        return new OptimizationJobCreateResult(
            OptimizationJobCreateOutcome.Created,
            ToSummary(stored));
    }

    public async Task<IReadOnlyList<OptimizationJobSummaryView>> ListAsync(
        string? state,
        CancellationToken cancellationToken = default)
    {
        OptimizationJobControlState? filter = null;
        if (!string.IsNullOrWhiteSpace(state)
            && Enum.TryParse<OptimizationJobControlState>(state, true, out var parsed))
            filter = parsed;

        var jobs = await _store.ListAsync(filter, cancellationToken);
        return jobs.Select(ToSummary).ToList();
    }

    public async Task<OptimizationJobDetailView?> FindAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.FindAsync(jobId, 3, cancellationToken);
        if (job is null) return null;

        var observedAt = _clock.GetUtcNow().UtcDateTime;
        var elapsed = CalculateElapsedSeconds(job, observedAt);
        return new OptimizationJobDetailView(
            ToSummary(job),
            elapsed,
            CalculateRemainingSeconds(job, elapsed),
            job.CompletedAt,
            job.LastProgressAt,
            job.ErrorMessage,
            job.Results);
    }

    public async Task<OptimizationJobSummaryView?> FindSummaryAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.FindAsync(jobId, 0, cancellationToken);
        return job is null ? null : ToSummary(job);
    }

    public async Task<OptimizationJobSummaryView?> UpdateSettingsAsync(
        int jobId,
        UpdateOptimizationJobSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        var minTrades = command.AutoApplyMinTrades is > 0
            ? command.AutoApplyMinTrades
            : null;
        if (command.AutoApplyBestResult.HasValue || minTrades.HasValue)
        {
            var updated = await _store.UpdateSettingsAsync(
                jobId,
                command.AutoApplyBestResult,
                minTrades,
                cancellationToken);
            if (!updated) return null;
        }

        var job = await _store.FindAsync(jobId, 0, cancellationToken);
        return job is null ? null : ToSummary(job);
    }

    public async Task<IReadOnlyList<OptimizeResultItem>?> GetResultsAsync(
        int jobId,
        int? top,
        CancellationToken cancellationToken = default)
    {
        var topCount = top is > 0 ? top.Value : DefaultTopResults;
        var job = await _store.FindAsync(jobId, topCount, cancellationToken);
        return job?.Results;
    }

    public async Task<OptimizationJobDeleteResult> DeleteAsync(
        int jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.FindAsync(jobId, 0, cancellationToken);
        if (job is null)
            return new OptimizationJobDeleteResult(OptimizationJobDeleteOutcome.NotFound);

        if (job.State is not (OptimizationJobControlState.Completed
            or OptimizationJobControlState.Cancelled
            or OptimizationJobControlState.Failed))
            return new OptimizationJobDeleteResult(
                OptimizationJobDeleteOutcome.InvalidState,
                job.State);

        if (await _store.TryDeleteAsync(jobId, job.State, cancellationToken))
            return new OptimizationJobDeleteResult(OptimizationJobDeleteOutcome.Deleted);

        var latest = await _store.FindAsync(jobId, 0, cancellationToken);
        return new OptimizationJobDeleteResult(
            latest is null
                ? OptimizationJobDeleteOutcome.NotFound
                : OptimizationJobDeleteOutcome.ConcurrentChange,
            latest?.State);
    }

    private static OptimizationJobSummaryView ToSummary(OptimizationJobRecord job) => new(
        job.Id,
        job.Name,
        job.State,
        job.TotalCombinations,
        job.TestedCombinations,
        CalculateProgress(job),
        job.CreatedAt,
        job.StartedAt,
        job.ContinuousMode,
        job.AutoApplyBestResult,
        job.AutoApplyMinTrades,
        job.AppliedResultCount,
        job.LastAutoAppliedAt,
        job.LastAutoAppliedResultId,
        job.LastAutoApplyMessage);

    private static decimal CalculateProgress(OptimizationJobRecord job)
    {
        if (job.TotalCombinations <= 0) return 0m;
        var raw = (decimal)job.TestedCombinations / job.TotalCombinations * 100m;
        return Math.Min(Math.Round(raw, 2), 100m);
    }

    private static double? CalculateElapsedSeconds(
        OptimizationJobRecord job,
        DateTime observedAt)
    {
        if (job.StartedAt is null) return null;
        return ((job.CompletedAt ?? observedAt) - job.StartedAt.Value).TotalSeconds;
    }

    private static double? CalculateRemainingSeconds(
        OptimizationJobRecord job,
        double? elapsedSeconds)
    {
        if (elapsedSeconds is null or <= 0 || job.TestedCombinations <= 0)
            return null;
        if (job.TotalCombinations <= job.TestedCombinations)
            return 0;

        var remaining = job.TotalCombinations - job.TestedCombinations;
        return elapsedSeconds.Value / job.TestedCombinations * remaining;
    }
}

public static class OptimizationCombinationCountPolicy
{
    public const long MaximumReportedCombinations = 1_000_000_000L;

    public static long Calculate(OptimizeParams parameters)
    {
        var axisSizes = new List<int>();
        AddNumeric(parameters.AtrStopMultiplier);
        AddNumeric(parameters.AtrTargetMultiplier);
        AddNumeric(parameters.MaxHoldingBars);
        AddNumeric(parameters.TrailingAtr);
        AddNumeric(parameters.PartialProfitR);
        AddNumeric(parameters.DefaultAllocationPercent);
        AddNumeric(parameters.CircuitBreakerConsecutiveLossLimit);
        AddNumeric(parameters.CircuitBreakerCooldownBars);
        AddNumeric(parameters.CircuitBreakerMaxDrawdownPercent);
        AddNumeric(parameters.ReentryCooldownAfterLoss);
        AddNumeric(parameters.ReentryCooldownAfterWin);
        AddNumeric(parameters.PortfolioMaxPositions);
        AddNumeric(parameters.PortfolioMaxSinglePercent);
        AddNumeric(parameters.PortfolioMaxEntriesPerDay);

        axisSizes.Add(CountOrOne(parameters.EntryLogicOptions));
        axisSizes.Add(CountOrOne(parameters.RequireBullRegimeOptions));
        axisSizes.Add(CountOrOne(parameters.EntryModeOptions));
        axisSizes.Add(CountOrOne(parameters.SizingModeOptions));
        axisSizes.Add(CountOrOne(parameters.ExitLogicOptions));
        axisSizes.Add(CountOrOne(parameters.TimeFrameOptions));

        axisSizes.AddRange((parameters.RuleParamOverrides ?? [])
            .Where(dimension => dimension.Values.Count > 0)
            .Select(dimension => dimension.Values.Count));
        axisSizes.AddRange((parameters.RuleFieldOverrides ?? [])
            .Select(dimension =>
                (dimension.NumericValues?.Count ?? 0)
                + (dimension.StringValues?.Count ?? 0))
            .Where(count => count > 0));

        var total = 1L;
        foreach (var size in axisSizes)
        {
            if (total > MaximumReportedCombinations / size)
                return MaximumReportedCombinations;
            total *= size;
        }

        return total;

        void AddNumeric(ParamRange? range)
        {
            var count = range?.Enumerate().Count() ?? 0;
            axisSizes.Add(count > 0 ? count : 1);
        }
    }

    private static int CountOrOne<T>(IReadOnlyCollection<T>? values) =>
        values is { Count: > 0 } ? values.Count : 1;
}
