namespace StockTrader.Application.Backtesting;

/// <summary>후보 종목과 보유 종목의 과거 공통 수익률만 사용해 상관관계 진입을 제한합니다.</summary>
public static class PortfolioCorrelationPolicy
{
    public const int DefaultWindow = 60;
    public const int MinimumReturnSamples = 10;

    public static bool ExceedsLimit(
        string candidateSymbol,
        IEnumerable<string> openSymbols,
        IReadOnlyDictionary<string, PreparedSymbolData> symbolData,
        DateTime asOf,
        decimal maximumCorrelation,
        int window = DefaultWindow)
    {
        if (maximumCorrelation <= 0) return false;
        if (!symbolData.TryGetValue(candidateSymbol, out var candidate)) return false;

        foreach (var openSymbol in openSymbols)
        {
            if (!symbolData.TryGetValue(openSymbol, out var existing)) continue;
            var correlation = ComputePearsonCorrelation(existing, candidate, asOf, window);
            if (correlation > (double)maximumCorrelation) return true;
        }

        return false;
    }

    public static double ComputePearsonCorrelation(
        PreparedSymbolData first,
        PreparedSymbolData second,
        DateTime asOf,
        int window = DefaultWindow)
    {
        var dates = first.TimestampToIndex.Keys
            .Where(date => date <= asOf && second.TimestampToIndex.ContainsKey(date))
            .OrderByDescending(date => date)
            .Take(window + 1)
            .OrderBy(date => date)
            .ToArray();
        var firstReturns = new List<double>(window);
        var secondReturns = new List<double>(window);

        for (var index = 1; index < dates.Length; index++)
        {
            var current = dates[index];
            var previous = dates[index - 1];
            if (!first.TimestampToIndex.TryGetValue(current, out var firstCurrent)
                || !first.TimestampToIndex.TryGetValue(previous, out var firstPrevious)
                || !second.TimestampToIndex.TryGetValue(current, out var secondCurrent)
                || !second.TimestampToIndex.TryGetValue(previous, out var secondPrevious))
            {
                continue;
            }
            if (first.Closes[firstPrevious] <= 0 || second.Closes[secondPrevious] <= 0) continue;

            firstReturns.Add((double)(
                (first.Closes[firstCurrent] - first.Closes[firstPrevious])
                / first.Closes[firstPrevious]));
            secondReturns.Add((double)(
                (second.Closes[secondCurrent] - second.Closes[secondPrevious])
                / second.Closes[secondPrevious]));
        }

        var sampleCount = Math.Min(firstReturns.Count, secondReturns.Count);
        if (sampleCount < MinimumReturnSamples) return 0;

        var firstMean = firstReturns.Take(sampleCount).Average();
        var secondMean = secondReturns.Take(sampleCount).Average();
        double covariance = 0, firstVariance = 0, secondVariance = 0;
        for (var index = 0; index < sampleCount; index++)
        {
            var firstDeviation = firstReturns[index] - firstMean;
            var secondDeviation = secondReturns[index] - secondMean;
            covariance += firstDeviation * secondDeviation;
            firstVariance += firstDeviation * firstDeviation;
            secondVariance += secondDeviation * secondDeviation;
        }

        var denominator = Math.Sqrt(firstVariance * secondVariance);
        return denominator > 0 ? covariance / denominator : 0;
    }
}
