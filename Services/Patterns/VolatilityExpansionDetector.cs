using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;
using static StockTrader.Services.Indicators.IndicatorService;

namespace StockTrader.Services.Patterns;

public class VolatilityExpansionDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly VolatilityExpansionConfig _config;

    public PatternType PatternType => PatternType.VolatilityExpansion;

    public VolatilityExpansionDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.VolatilityExpansion;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 implementation
        if (bars.Length < _config.BollingerPeriod + 1)
            return Task.FromResult<PatternSignal?>(null);
        // Regime filter removed: volatility expansion can occur in any market regime

        var closes = ExtractCloses(bars);
        var (upper, middle, lower) = _indicators.BollingerBands(
            closes, _config.BollingerPeriod, _config.StdDevMultiplier);

        var curr = bars[^1];
        if (curr.Close <= upper[^1]) return Task.FromResult<PatternSignal?>(null);

        var atr = _indicators.ATR(bars);
        var currentAtr = atr[^1];
        // Guard against zero ATR (can occur with flat/illiquid bars) to prevent division by zero
        if (currentAtr <= 0) return Task.FromResult<PatternSignal?>(null);

        // 스탑 로직: ATR 스탑과 구조적 스탑(BB Middle) 중 낮은 쪽(넓은 스탑) 사용.
        // BB Upper 돌파는 변동성 확대 신호이므로 조기 손절 방지를 위해 넓은 스탑 적용.
        var atrStop = curr.Close - currentAtr * _config.AtrStopMultiplier;
        var structureStop = middle[^1];
        var stopLoss = Math.Min(atrStop, structureStop);
        // 저가주 보호: 스탑이 0 이하면 ATR 스탑만 사용
        if (stopLoss <= 0) stopLoss = atrStop;
        if (stopLoss <= 0) return Task.FromResult<PatternSignal?>(null);

        var atrTarget = curr.Close + currentAtr * _config.AtrTargetMultiplier;
        var structureTarget = curr.Close + (curr.Close - middle[^1]);
        var targetPrice = Math.Max(atrTarget, structureTarget);

        var signal = new PatternSignal
        {
            Symbol = symbol,
            PatternType = PatternType.VolatilityExpansion,
            DetectedAt = DateTime.UtcNow,
            EntryPrice = curr.Close,
            StopLossPrice = stopLoss,
            TargetPrice = targetPrice,
            Confidence = Math.Min(1.0m, (curr.Close - upper[^1]) / currentAtr),
            Details = $"BB Upper Break, Close: {curr.Close:F2}, Upper: {upper[^1]:F2}",
            IsActive = true
        };
        return Task.FromResult<PatternSignal?>(signal);
    }
}
