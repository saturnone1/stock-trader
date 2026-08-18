using StockTrader.Application.MachineLearning;
using StockTrader.Models;

namespace StockTrader.Services.ML;

/// <summary>완료된 과거 일봉만 사용해 레짐 학습·예측 피처를 동일하게 계산합니다.</summary>
internal static class MarketRegimeFeatureFactory
{
    private const int FirstFeatureBarIndex = 25;
    private const int MinimumPredictionBars = 30;
    private const int LongReturnBars = 20;
    private const int MediumReturnBars = 10;
    private const int ShortReturnBars = 5;
    private const int VolatilityBars = 5;
    private const int MovingAverageBars = 20;
    private const int MovingAverageComparisonBars = 5;
    private const int RsiBars = 14;

    public static IReadOnlyList<MarketRegimeFeatures> CreateTrainingSamples(
        IReadOnlyList<OhlcvBar> bars,
        DateTime asOf)
    {
        var ordered = Prepare(bars, asOf);
        var closes = ordered.Select(bar => (double)bar.Close).ToArray();
        var volumes = ordered.Select(bar => (double)bar.Volume).ToArray();
        var samples = new List<MarketRegimeFeatures>(
            Math.Max(0, ordered.Length - FirstFeatureBarIndex));
        for (var index = FirstFeatureBarIndex; index < ordered.Length; index++)
        {
            var features = CreateAt(closes, volumes, index);
            if (features is not null) samples.Add(features);
        }
        return samples;
    }

    public static MarketRegimeFeatures? CreateLatest(
        IReadOnlyList<OhlcvBar> bars,
        DateTime asOf)
    {
        var ordered = Prepare(bars, asOf);
        var closes = ordered.Select(bar => (double)bar.Close).ToArray();
        var volumes = ordered.Select(bar => (double)bar.Volume).ToArray();
        return ordered.Length >= MinimumPredictionBars
            ? CreateAt(closes, volumes, ordered.Length - 1)
            : null;
    }

    private static OhlcvBar[] Prepare(IReadOnlyList<OhlcvBar> bars, DateTime asOf) =>
        bars.Where(bar => bar.Timestamp <= asOf)
            .OrderBy(bar => bar.Timestamp)
            .ToArray();

    private static MarketRegimeFeatures? CreateAt(
        double[] closes,
        double[] volumes,
        int index)
    {
        if (Enumerable.Range(index - 24, 25).Any(offset => closes[offset] <= 0))
            return null;

        var return5 = Change(closes, index, ShortReturnBars);
        var return10 = Change(closes, index, MediumReturnBars);
        var return20 = Change(closes, index, LongReturnBars);

        var dailyReturns = Enumerable.Range(index - VolatilityBars + 1, VolatilityBars)
            .Select(offset => Change(closes, offset, 1))
            .ToArray();
        var averageReturn = dailyReturns.Average();
        var volatility = Math.Sqrt(dailyReturns.Average(
            value => Math.Pow(value - averageReturn, 2)));

        var volume5 = volumes.Skip(index - ShortReturnBars + 1)
            .Take(ShortReturnBars).Average();
        var volume20 = volumes.Skip(index - LongReturnBars + 1)
            .Take(LongReturnBars).Average();
        var volumeChange = volume20 > 0 ? (volume5 - volume20) / volume20 : 0;

        var averageNow = closes.Skip(index - MovingAverageBars + 1)
            .Take(MovingAverageBars).Average();
        var averageBefore = closes
            .Skip(index - MovingAverageBars - MovingAverageComparisonBars + 1)
            .Take(MovingAverageBars).Average();
        var averageSlope = averageBefore > 0
            ? (averageNow - averageBefore) / averageBefore
            : 0;

        var changes = Enumerable.Range(index - RsiBars + 1, RsiBars)
            .Select(offset => closes[offset] - closes[offset - 1])
            .ToArray();
        var averageGain = changes.Where(change => change > 0).Sum() / RsiBars;
        var averageLoss = changes.Where(change => change < 0).Sum(Math.Abs) / RsiBars;
        var rsi = averageLoss == 0
            ? 1d
            : (100d - 100d / (1d + averageGain / averageLoss)) / 100d;

        return new MarketRegimeFeatures(
            (float)return5,
            (float)return10,
            (float)return20,
            (float)volatility,
            (float)volumeChange,
            (float)averageSlope,
            (float)rsi);
    }

    private static double Change(double[] closes, int index, int bars) =>
        (closes[index] - closes[index - bars]) / closes[index - bars];
}
