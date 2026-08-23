using StockTrader.Engine.MarketData;

namespace StockTrader.Engine.Rules;

/// <summary>외부 지표 라이브러리에 없는 규칙 지표의 결정적 수학 구현.</summary>
internal static class RuleIndicatorMath
{
    internal static decimal CalculateVolatility(
        decimal[] closes,
        int endIndex,
        int period,
        TimeFrame timeFrame)
    {
        if (endIndex < period) return 0;
        var returns = new decimal[period];
        for (var i = 0; i < period; i++)
        {
            var previous = closes[endIndex - period + i];
            var current = closes[endIndex - period + i + 1];
            returns[i] = previous == 0 ? 0 : (current - previous) / previous;
        }
        var mean = returns.Average();
        var variance = returns.Average(value => (value - mean) * (value - mean));
        return (decimal)Math.Sqrt((double)variance)
            * (decimal)Math.Sqrt((double)TimeFrameCatalog.AnnualizationPeriods(timeFrame))
            * 100m;
    }

    internal static decimal[] ComputeAdx(PriceBar[] bars, int period)
    {
        var count = bars.Length;
        var adx = new decimal[count];
        if (count < period + 1) return adx;

        var plusDirectionalMovement = new decimal[count];
        var minusDirectionalMovement = new decimal[count];
        var trueRange = new decimal[count];
        for (var i = 1; i < count; i++)
        {
            var upMove = bars[i].High - bars[i - 1].High;
            var downMove = bars[i - 1].Low - bars[i].Low;
            plusDirectionalMovement[i] = upMove > downMove && upMove > 0 ? upMove : 0;
            minusDirectionalMovement[i] = downMove > upMove && downMove > 0 ? downMove : 0;
            trueRange[i] = Math.Max(
                bars[i].High - bars[i].Low,
                Math.Max(
                    Math.Abs(bars[i].High - bars[i - 1].Close),
                    Math.Abs(bars[i].Low - bars[i - 1].Close)));
        }

        decimal smoothPlus = 0;
        decimal smoothMinus = 0;
        decimal smoothTrueRange = 0;
        for (var i = 1; i <= period; i++)
        {
            smoothPlus += plusDirectionalMovement[i];
            smoothMinus += minusDirectionalMovement[i];
            smoothTrueRange += trueRange[i];
        }

        var directionalIndex = new decimal[count];
        for (var i = period; i < count; i++)
        {
            if (i > period)
            {
                smoothPlus = smoothPlus - smoothPlus / period + plusDirectionalMovement[i];
                smoothMinus = smoothMinus - smoothMinus / period + minusDirectionalMovement[i];
                smoothTrueRange = smoothTrueRange - smoothTrueRange / period + trueRange[i];
            }
            if (smoothTrueRange == 0) continue;
            var plusIndex = smoothPlus / smoothTrueRange * 100;
            var minusIndex = smoothMinus / smoothTrueRange * 100;
            var sum = plusIndex + minusIndex;
            directionalIndex[i] = sum == 0 ? 0 : Math.Abs(plusIndex - minusIndex) / sum * 100;
        }

        decimal initialSum = 0;
        for (var i = period; i < period * 2 && i < count; i++) initialSum += directionalIndex[i];
        if (period * 2 <= count) adx[period * 2 - 1] = initialSum / period;
        for (var i = period * 2; i < count; i++)
            adx[i] = (adx[i - 1] * (period - 1) + directionalIndex[i]) / period;
        return adx;
    }

    internal static (decimal[] K, decimal[] D) ComputeStochastic(
        PriceBar[] bars,
        int period,
        int smooth)
    {
        var count = bars.Length;
        var k = new decimal[count];
        var d = new decimal[count];
        for (var i = period - 1; i < count; i++)
        {
            decimal highest = 0;
            var lowest = decimal.MaxValue;
            for (var j = i - period + 1; j <= i; j++)
            {
                if (bars[j].High > highest) highest = bars[j].High;
                if (bars[j].Low < lowest) lowest = bars[j].Low;
            }
            var range = highest - lowest;
            k[i] = range == 0 ? 50 : (bars[i].Close - lowest) / range * 100;
        }

        for (var i = period - 1 + smooth - 1; i < count; i++)
        {
            decimal sum = 0;
            for (var j = i - smooth + 1; j <= i; j++) sum += k[j];
            d[i] = sum / smooth;
        }
        return (k, d);
    }

    internal static decimal CalculateRollingVwap(PriceBar[] bars, int index, int period)
    {
        if (index < period - 1) return 0;
        decimal priceVolumeTotal = 0;
        long volumeTotal = 0;
        for (var i = index - period + 1; i <= index; i++)
        {
            var typicalPrice = (bars[i].High + bars[i].Low + bars[i].Close) / 3m;
            priceVolumeTotal += typicalPrice * bars[i].Volume;
            volumeTotal += bars[i].Volume;
        }
        return volumeTotal == 0 ? 0 : priceVolumeTotal / volumeTotal;
    }

    internal static decimal CalculateCci(PriceBar[] bars, int index, int period)
    {
        if (index < period) return 0;
        var typicalPrices = new decimal[period];
        for (var i = 0; i < period; i++)
        {
            var bar = bars[index - period + 1 + i];
            typicalPrices[i] = (bar.High + bar.Low + bar.Close) / 3m;
        }
        var mean = typicalPrices.Average();
        var meanAbsoluteDeviation = typicalPrices.Average(value => Math.Abs(value - mean));
        return meanAbsoluteDeviation == 0
            ? 0
            : (typicalPrices[^1] - mean) / (0.015m * meanAbsoluteDeviation);
    }

    internal static decimal CalculateWilliamsR(PriceBar[] bars, int index, int period)
    {
        if (index < period - 1) return -50;
        decimal highest = 0;
        var lowest = decimal.MaxValue;
        for (var i = index - period + 1; i <= index; i++)
        {
            if (bars[i].High > highest) highest = bars[i].High;
            if (bars[i].Low < lowest) lowest = bars[i].Low;
        }
        var range = highest - lowest;
        return range == 0 ? -50 : (highest - bars[index].Close) / range * -100;
    }

    internal static decimal CalculateCmf(PriceBar[] bars, int index, int period)
    {
        if (index < period) return 0;
        decimal moneyFlowVolumeTotal = 0;
        decimal volumeTotal = 0;
        for (var i = index - period + 1; i <= index; i++)
        {
            var range = bars[i].High - bars[i].Low;
            var multiplier = range == 0
                ? 0
                : ((bars[i].Close - bars[i].Low) - (bars[i].High - bars[i].Close)) / range;
            moneyFlowVolumeTotal += multiplier * bars[i].Volume;
            volumeTotal += bars[i].Volume;
        }
        return volumeTotal == 0 ? 0 : moneyFlowVolumeTotal / volumeTotal;
    }
}
