using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class EarningsDriftDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly EarningsDriftConfig _config;

    public PatternType PatternType => PatternType.EarningsDrift;

    public EarningsDriftDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.EarningsDrift;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // TODO: Phase 2 - requires earnings calendar data integration
        return Task.FromResult<PatternSignal?>(null);
    }
}
