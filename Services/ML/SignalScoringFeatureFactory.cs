using StockTrader.Application.MachineLearning;
using StockTrader.Application.Strategies;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.ML;

/// <summary>공통 지표 계산기를 사용해 진입 시점 피처 한 벌을 만듭니다.</summary>
internal sealed class SignalScoringFeatureFactory(IIndicatorService indicators)
{
    private const int RsiPeriod = 14;
    private const int BollingerPeriod = 20;
    private const decimal BollingerDeviation = 2m;
    private const int VolumePeriod = 20;
    private const decimal MaximumVolumeRatio = 5m;
    private const decimal MaximumAtrFraction = 0.2m;
    private const decimal MaximumLongMaDistance = 0.5m;
    private const decimal MaximumRiskReward = 5m;

    public SignalScoringFeatures? Create(
        PatternSignal signal,
        IReadOnlyList<OhlcvBar> bars,
        MarketRegime regime,
        decimal historicalWinRate)
    {
        if (bars.Count == 0)
            return null;

        var asOf = signal.SignalBarAt ?? bars.MaxBy(bar => bar.Timestamp)?.Timestamp;
        var ordered = bars
            .Where(bar => !asOf.HasValue || bar.Timestamp <= asOf.Value)
            .OrderBy(bar => bar.Timestamp)
            .ToArray();
        if (ordered.Length < BollingerPeriod || ordered[^1].Close <= 0)
            return null;

        var closes = ordered.Select(bar => bar.Close).ToArray();
        var currentClose = closes[^1];
        var rsi = indicators.RSI(closes, RsiPeriod)[^1] / 100m;
        var bands = indicators.BollingerBands(
            closes, BollingerPeriod, BollingerDeviation);
        var width = bands.Upper[^1] - bands.Lower[^1];
        var bollingerPosition = width > 0
            ? (currentClose - bands.Lower[^1]) / width
            : 0.5m;
        var averageVolume = ordered.TakeLast(VolumePeriod)
            .Average(bar => (decimal)bar.Volume);
        var volumeRatio = averageVolume > 0
            ? ordered[^1].Volume / averageVolume
            : 1m;
        var atr = indicators.ATR(ordered, StrategyEvaluationPolicy.EntryAtrPeriod)[^1];
        var atrFraction = atr > 0 ? atr / currentClose : 0m;
        var hasLongHistory = ordered.Length >= StrategyEvaluationPolicy.RegimeTrendBars;
        var longAverage = hasLongHistory
            ? indicators.SMA(closes, StrategyEvaluationPolicy.RegimeTrendBars)[^1]
            : 0m;
        var priceVsLongAverage = longAverage > 0
            ? currentClose / longAverage - 1m
            : 0m;
        var riskReward = RiskRewardRatioPolicy.CalculateWithAbsoluteStopDistance(
            signal.EntryPrice,
            signal.StopLossPrice,
            signal.TargetPrice);

        return new SignalScoringFeatures(
            SignalScoringFeatureSchema.CurrentVersion,
            (float)signal.PatternType,
            (float)Math.Clamp(rsi, 0m, 1m),
            (float)Math.Clamp(bollingerPosition, 0m, 1m),
            (float)Math.Clamp(volumeRatio, 0m, MaximumVolumeRatio),
            MapRegime(regime),
            (float)Math.Clamp(atrFraction, 0m, MaximumAtrFraction),
            (float)Math.Clamp(historicalWinRate, 0m, 1m),
            (float)Math.Clamp(riskReward, 0m, MaximumRiskReward),
            (float)Math.Clamp(
                priceVsLongAverage,
                -MaximumLongMaDistance,
                MaximumLongMaDistance),
            hasLongHistory ? 1f : 0f);
    }

    private static float MapRegime(MarketRegime regime) => regime.MlClusterId >= 0
        ? regime.MlClusterId
        : regime.RegimeLabel switch
        {
            MarketRegimeTrendPolicy.BullishLabel
                or MarketRegimeClusterCatalog.Bullish => 0f,
            MarketRegimeTrendPolicy.BearishLabel
                or MarketRegimeClusterCatalog.Bearish => 1f,
            MarketRegimeClusterCatalog.Sideways => 2f,
            MarketRegimeClusterCatalog.HighVolatility => 3f,
            _ => -1f,
        };
}
