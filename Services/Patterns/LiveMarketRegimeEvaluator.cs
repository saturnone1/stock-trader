using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

public sealed class LiveMarketRegimeEvaluator : ILiveMarketRegimeEvaluator
{
    public MarketRegime Evaluate(IReadOnlyList<OhlcvBar> bars, DateTime observedAt) =>
        MarketRegimeTrendPolicy.Evaluate(bars, observedAt);
}
