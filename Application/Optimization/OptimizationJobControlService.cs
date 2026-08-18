namespace StockTrader.Application.Optimization;

public enum OptimizationJobControlState
{
    Pending,
    Running,
    Paused,
    Completed,
    Cancelled,
    Failed
}

public enum OptimizationJobControlCommand
{
    Pause,
    Resume,
    Cancel
}

public enum OptimizationJobControlOutcome
{
    Applied,
    NotFound,
    InvalidState,
    ConcurrentChange
}

public sealed record OptimizationJobControlResult(
    OptimizationJobControlOutcome Outcome,
    OptimizationJobControlState? State = null);

public sealed record OptimizationJobStateTransition(
    OptimizationJobControlState From,
    OptimizationJobControlState To,
    DateTime? CompletedAt);

/// <summary>작업 제어 상태를 저장하는 목적별 포트입니다.</summary>
public interface IOptimizationJobControlStore
{
    Task<OptimizationJobControlState?> GetStateAsync(
        int jobId,
        CancellationToken cancellationToken = default);

    Task<bool> TryTransitionAsync(
        int jobId,
        OptimizationJobStateTransition transition,
        CancellationToken cancellationToken = default);

    Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default);
}

/// <summary>사용자 작업 제어와 시작 복구를 조정하는 애플리케이션 사용 사례입니다.</summary>
public sealed class OptimizationJobControlService
{
    private readonly IOptimizationJobControlStore _store;

    public OptimizationJobControlService(IOptimizationJobControlStore store)
    {
        _store = store;
    }

    public async Task<OptimizationJobControlResult> ApplyAsync(
        int jobId,
        OptimizationJobControlCommand command,
        DateTime observedAt,
        CancellationToken cancellationToken = default)
    {
        var state = await _store.GetStateAsync(jobId, cancellationToken);
        if (!state.HasValue)
            return new OptimizationJobControlResult(OptimizationJobControlOutcome.NotFound);

        var transition = OptimizationJobControlPolicy.Resolve(state.Value, command, observedAt);
        if (transition is null)
            return new OptimizationJobControlResult(
                OptimizationJobControlOutcome.InvalidState,
                state);

        if (await _store.TryTransitionAsync(jobId, transition, cancellationToken))
            return new OptimizationJobControlResult(
                OptimizationJobControlOutcome.Applied,
                transition.To);

        var latest = await _store.GetStateAsync(jobId, cancellationToken);
        return new OptimizationJobControlResult(
            latest.HasValue
                ? OptimizationJobControlOutcome.ConcurrentChange
                : OptimizationJobControlOutcome.NotFound,
            latest);
    }

    public Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default) =>
        _store.RecoverInterruptedAsync(cancellationToken);
}

public static class OptimizationJobControlPolicy
{
    public static OptimizationJobStateTransition? Resolve(
        OptimizationJobControlState state,
        OptimizationJobControlCommand command,
        DateTime observedAt) => command switch
    {
        OptimizationJobControlCommand.Pause
            when state is OptimizationJobControlState.Pending
                or OptimizationJobControlState.Running =>
            new OptimizationJobStateTransition(
                state, OptimizationJobControlState.Paused, null),

        OptimizationJobControlCommand.Resume
            when state == OptimizationJobControlState.Paused =>
            new OptimizationJobStateTransition(
                state, OptimizationJobControlState.Pending, null),

        OptimizationJobControlCommand.Cancel
            when state is not (OptimizationJobControlState.Completed
                or OptimizationJobControlState.Cancelled
                or OptimizationJobControlState.Failed) =>
            new OptimizationJobStateTransition(
                state, OptimizationJobControlState.Cancelled, observedAt),

        _ => null
    };
}
