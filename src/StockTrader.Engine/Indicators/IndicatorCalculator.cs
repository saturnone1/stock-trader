using StockTrader.Engine.MarketData;

namespace StockTrader.Engine.Indicators;

public sealed partial class IndicatorCalculator
{
    public decimal[] SMA(decimal[] closes, int period)
    {
        var result = new decimal[closes.Length];
        if (closes.Length == 0 || period <= 0) return result;

        // Sliding window: O(n) instead of O(n*period).
        // Seed the initial window sum for index [period-1].
        decimal windowSum = 0;
        for (int i = 0; i < period - 1 && i < closes.Length; i++)
            windowSum += closes[i];

        for (int i = period - 1; i < closes.Length; i++)
        {
            windowSum += closes[i];
            result[i] = windowSum / period;
            // Slide: subtract the element falling out of the window on the next step.
            windowSum -= closes[i - period + 1];
        }
        return result;
    }

    public decimal[] EMA(decimal[] closes, int period)
    {
        var result = new decimal[closes.Length];
        if (closes.Length == 0) return result;

        decimal multiplier = 2.0m / (period + 1);
        result[0] = closes[0];
        for (int i = 1; i < closes.Length; i++)
        {
            result[i] = (closes[i] - result[i - 1]) * multiplier + result[i - 1];
        }
        return result;
    }

    public decimal[] RSI(decimal[] closes, int period = 14)
    {
        var result = new decimal[closes.Length];
        if (closes.Length <= period) return result;

        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0) avgGain += change;
            else avgLoss += Math.Abs(change);
        }
        avgGain /= period;
        avgLoss /= period;

        result[period] = avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss));

        for (int i = period + 1; i < closes.Length; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0;
            var loss = change < 0 ? Math.Abs(change) : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;

            result[i] = avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss));
        }
        return result;
    }

    public decimal[] CumulativeRsi(decimal[] closes, int rsiPeriod, int cumulativePeriod)
    {
        var result = new decimal[closes.Length];
        if (closes.Length == 0 || rsiPeriod <= 0 || cumulativePeriod <= 0)
            return result;

        var rsi = RSI(closes, rsiPeriod);
        var firstValidIndex = rsiPeriod + cumulativePeriod - 1;
        decimal windowSum = 0;

        for (int i = 0; i < rsi.Length; i++)
        {
            windowSum += rsi[i];
            if (i >= cumulativePeriod)
                windowSum -= rsi[i - cumulativePeriod];

            if (i >= firstValidIndex)
                result[i] = windowSum;
        }

        return result;
    }

    public (decimal[] Upper, decimal[] Middle, decimal[] Lower) BollingerBands(
        decimal[] closes, int period = 20, decimal stdDevMultiplier = 2.0m)
    {
        var middle = SMA(closes, period);
        var upper = new decimal[closes.Length];
        var lower = new decimal[closes.Length];

        // Compute std-dev using the computational formula:
        //   variance = (sum_of_squares / n) - mean^2
        // This allows a sliding window update in O(1) per element — O(n) total.
        // SumSq and SumX slide together with the window.
        if (closes.Length < period) return (upper, middle, lower);

        decimal sumX = 0, sumSq = 0;
        for (int i = 0; i < period; i++)
        {
            sumX  += closes[i];
            sumSq += closes[i] * closes[i];
        }

        for (int i = period - 1; i < closes.Length; i++)
        {
            // On the first iteration (i == period-1) the window is already seeded.
            // On subsequent iterations slide the window forward by one element.
            if (i > period - 1)
            {
                decimal outVal = closes[i - period];
                decimal inVal  = closes[i];
                sumX  += inVal  - outVal;
                sumSq += inVal  * inVal - outVal * outVal;
            }

            var mean = sumX / period;
            // Variance = E[X^2] - (E[X])^2; clamp to 0 to avoid negative from floating-point error.
            var variance = Math.Max(0m, sumSq / period - mean * mean);
            var stdDev   = (decimal)Math.Sqrt((double)variance);
            upper[i] = middle[i] + stdDevMultiplier * stdDev;
            lower[i] = middle[i] - stdDevMultiplier * stdDev;
        }

        return (upper, middle, lower);
    }
}
