using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.TradingCore.Execution;

public static class TradingProjectionExecutionContextPolicy
{
    public static TradingPositionProjection Apply(
        TradingPositionProjection position,
        TradingPositionExecutionContext context)
    {
        if (position.ExecutionContext is not null
            && !string.Equals(position.ExecutionContext.ExecutionArtifact.ArtifactId,
                context.ExecutionArtifact.ArtifactId, StringComparison.Ordinal))
            throw new InvalidOperationException("position-execution-context-conflict");
        return position with { ExecutionContext = position.ExecutionContext ?? context };
    }
}
