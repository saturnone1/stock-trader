using StockTrader.Application.Optimization;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

internal static class OptimizationJobStateMapper
{
    public static OptimizationJobControlState ToApplication(OptimizationJobStatus state) =>
        state switch
        {
            OptimizationJobStatus.Pending => OptimizationJobControlState.Pending,
            OptimizationJobStatus.Running => OptimizationJobControlState.Running,
            OptimizationJobStatus.Paused => OptimizationJobControlState.Paused,
            OptimizationJobStatus.Completed => OptimizationJobControlState.Completed,
            OptimizationJobStatus.Cancelled => OptimizationJobControlState.Cancelled,
            OptimizationJobStatus.Failed => OptimizationJobControlState.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };

    public static OptimizationJobStatus ToStorage(OptimizationJobControlState state) =>
        state switch
        {
            OptimizationJobControlState.Pending => OptimizationJobStatus.Pending,
            OptimizationJobControlState.Running => OptimizationJobStatus.Running,
            OptimizationJobControlState.Paused => OptimizationJobStatus.Paused,
            OptimizationJobControlState.Completed => OptimizationJobStatus.Completed,
            OptimizationJobControlState.Cancelled => OptimizationJobStatus.Cancelled,
            OptimizationJobControlState.Failed => OptimizationJobStatus.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
}
