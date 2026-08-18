using StockTrader.Application.Strategies;
using StockTrader.Application.Trading;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public sealed class LiveMarketRegimeEvaluator(IIndicatorService indicators)
    : ILiveMarketRegimeEvaluator
{
    public MarketRegime Evaluate(IReadOnlyList<OhlcvBar> bars, DateTime observedAt)
    {
        var regime = new MarketRegime { AsOf = observedAt };
        if (bars.Count < StrategyEvaluationPolicy.RegimeTrendBars)
            return regime;

        var closes = bars.Select(bar => bar.Close).ToArray();
        var trend = indicators.SMA(closes, StrategyEvaluationPolicy.RegimeTrendBars);
        regime.SpyPrice = closes[^1];
        regime.Spy200Ma = trend[^1];
        regime.SpyAbove200Ma = regime.SpyPrice > regime.Spy200Ma;
        regime.RegimeLabel = regime.SpyAbove200Ma ? "강세" : "약세";
        return regime;
    }
}
