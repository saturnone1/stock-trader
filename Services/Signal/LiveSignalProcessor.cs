using StockTrader.Application.Trading;
using StockTrader.Models;
using StockTrader.Services.Order;

namespace StockTrader.Services.Signal;

public sealed class LiveSignalProcessor(
    IPatternSignalStore signals,
    ISignalService recommendations,
    IOrderService orders) : ILiveSignalProcessor
{
    public async Task ProcessAsync(
        IReadOnlyList<PatternSignal> detected,
        CancellationToken ct = default)
    {
        if (detected.Count == 0)
            return;

        await signals.AddSignalsBatchAsync(detected, ct);
        var evaluated = await recommendations.EvaluateSignalsAsync(detected.ToList(), ct);
        foreach (var recommendation in evaluated)
            await orders.PlaceOrderAsync(recommendation, ct);
    }
}
