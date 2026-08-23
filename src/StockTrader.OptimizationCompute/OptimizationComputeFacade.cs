using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockTrader.Application.Backtesting;
using StockTrader.Application.Optimization;
using StockTrader.Optimization.Protocol;
using StockTrader.ServiceContracts.Optimization;
using StockTrader.Services.Backtest;
using StockTrader.Services.Patterns;

namespace StockTrader.Optimization.Compute;

public static class OptimizationComputeFacade
{
    public static async Task<OptimizationWorkerComputeResult> ExecuteAsync(
        OptimizationWorkLease lease,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        var error = OptimizationLeaseCompatibilityPolicy.Error(lease)
            ?? StrategyExecutionArtifactPolicy.CompatibilityError(lease.Input.Strategy);
        if (error is not null)
            throw new InvalidOperationException($"Incompatible optimization lease: {error}");
        if (lease.Purpose != OptimizationWorkerContractCatalog.ShadowComputePurpose)
            throw new InvalidOperationException($"Unsupported compute purpose: {lease.Purpose}");

        var request = OptimizeRequestJsonCodec.Deserialize(lease.Input.RequestJson)
            ?? throw new InvalidOperationException("Optimization request JSON is invalid.");
        var context = PreparedEvaluationContextMapper.Map(request, lease.Input);
        var settings = Options.Create(context.PatternSettings);
        var entryProcessor = new BacktestSignalEntryProcessor(
            NullLogger<BacktestSignalEntryProcessor>.Instance);
        var runner = new BacktestPreparedSimulationRunner(
            new PreparedBacktestDataSlicer(),
            new BacktestSimulationEngine(entryProcessor),
            settings);
        var evaluator = new OptimizationCandidateEvaluator(
            new CustomStrategyDetectorFactory(),
            runner,
            settings,
            NullLogger<OptimizationCandidateEvaluator>.Instance);
        var service = new BacktestOptimizationService(
            new PreparedContextPreparer(context),
            evaluator,
            NullLogger<BacktestOptimizationService>.Instance);
        var response = await service.RunAsync(request, ct);
        return OptimizationComputeResultMapper.Map(lease.Input.InputHash, response);
    }
}
