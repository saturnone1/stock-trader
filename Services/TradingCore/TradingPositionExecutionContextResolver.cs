using System.Text.Json;
using StockTrader.Application.Execution;
using StockTrader.Application.Strategies;
using StockTrader.Configuration;
using StockTrader.Optimization.Protocol;
using StockTrader.Models;
using StockTrader.Services.Order;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

internal sealed record ResolvedTradingPositionExecution(
    CompiledStrategy? Strategy,
    ImmutableLivePositionSettings Settings);

internal sealed class TradingPositionExecutionContextResolver
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ResolvedTradingPositionExecution Resolve(TradingPositionExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var artifact = context.ExecutionArtifact;
        var error = TradingExecutionArtifactPolicy.Error(artifact);
        if (error is not null)
            throw new InvalidOperationException(error);
        if (artifact.Kind == TradingExecutionArtifactKinds.StrategyDocument)
            return ResolveStrategy(artifact);
        if (artifact.Kind != TradingExecutionArtifactKinds.BuiltInPattern)
            throw new InvalidOperationException("unsupported-position-execution-artifact-kind");

        var snapshot = JsonSerializer.Deserialize<BuiltInSnapshot>(
            artifact.BuiltInSettingsJson, Json)
            ?? throw new InvalidOperationException("empty-built-in-position-execution-snapshot");
        var pattern = TradingCoreProjectionMapper.Pattern(artifact.PatternCode, null);
        return new ResolvedTradingPositionExecution(
            null,
            new ImmutableLivePositionSettings(
                snapshot.ExitPolicy,
                pattern == PatternType.CumulativeRsi2
                    ? snapshot.PatternConfiguration.Deserialize<CumulativeRsi2Config>(Json)
                    : null,
                pattern == PatternType.Tqqq200Sma
                    ? snapshot.PatternConfiguration.Deserialize<Tqqq200SmaConfig>(Json)
                    : null));
    }

    private static ResolvedTradingPositionExecution ResolveStrategy(
        TradingStrategyExecutionArtifact artifact)
    {
        var strategyArtifact = artifact.StrategyDocument
            ?? throw new InvalidOperationException("missing-strategy-position-execution-artifact");
        var compatibility = StrategyExecutionArtifactPolicy.CompatibilityError(strategyArtifact);
        if (compatibility is not null)
            throw new InvalidOperationException(compatibility);
        var document = JsonSerializer.Deserialize<StrategyDocument>(
            strategyArtifact.StrategyDocumentJson, Json)
            ?? throw new InvalidOperationException("empty-strategy-position-execution-document");
        var compilation = StrategyCompiler.Compile(document);
        var strategy = compilation.Strategy
            ?? throw new InvalidOperationException(string.Join(" ", compilation.Errors));
        return new ResolvedTradingPositionExecution(
            strategy,
            new ImmutableLivePositionSettings(
                LongPositionExitPolicyCatalog.ForCustom(document)));
    }

    private sealed record BuiltInSnapshot(
        JsonElement PatternConfiguration,
        LongPositionExitPolicy ExitPolicy);
}
