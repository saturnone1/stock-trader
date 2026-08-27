using StockTrader.Models;

namespace StockTrader.Services.Order;

internal static class LivePositionAtrPolicy
{
    public static decimal Calculate(IReadOnlyList<OhlcvBar> bars, int period)
    {
        if (bars.Count < period + 1) return 0;
        return Enumerable.Range(bars.Count - period, period)
            .Select(index =>
            {
                var bar = bars[index];
                var previousClose = bars[index - 1].Close;
                return Math.Max(
                    bar.High - bar.Low,
                    Math.Max(
                        Math.Abs(bar.High - previousClose),
                        Math.Abs(bar.Low - previousClose)));
            })
            .Average();
    }
}
