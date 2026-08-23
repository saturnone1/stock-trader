using StockTrader.Application.Optimization;

namespace StockTrader.Optimization.Compute;

internal sealed class PreparedContextPreparer(OptimizationEvaluationContext context)
    : IOptimizationEvaluationContextPreparer
{
    public Task<OptimizationPreparationResult> PrepareAsync(
        OptimizeRequest request,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OptimizationPreparationResult.Success(context));
    }
}
