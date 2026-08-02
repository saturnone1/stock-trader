using StockTrader.Models;

namespace StockTrader.Services.Signal;

public interface ISignalService
{
    Task<List<TradeRecommendation>> EvaluateSignalsAsync(
        List<PatternSignal> signals, CancellationToken ct = default);
}
