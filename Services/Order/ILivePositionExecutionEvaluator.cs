using StockTrader.Application.Strategies;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.Order;

public interface ILivePositionExecutionEvaluator
{
    Task<LivePositionExecutionDecision> EvaluateAsync(
        Position position,
        CompiledStrategy? customStrategy,
        IOhlcvRepository repository,
        PatternParameterOverrides? liveOverrides,
        CancellationToken ct = default,
        decimal currentEquity = 0m,
        int maxTotalPositions = 0);
}
