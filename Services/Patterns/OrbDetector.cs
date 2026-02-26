using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class OrbDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly OrbConfig _config;

    public PatternType PatternType => PatternType.OpeningRangeBreakout;

    public OrbDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.OpeningRangeBreakout;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 - requires intraday 1-min bars
        return Task.FromResult<PatternSignal?>(null);
    }
}
