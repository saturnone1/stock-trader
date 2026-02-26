using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Indicators;

namespace StockTrader.Services.Patterns;

public class IndexRegimeFilterDetector : IPatternDetector
{
    private readonly IIndicatorService _indicators;
    private readonly IndexRegimeConfig _config;

    public PatternType PatternType => PatternType.IndexRegimeFilter;

    public IndexRegimeFilterDetector(IIndicatorService indicators, IOptions<PatternSettings> settings)
    {
        _indicators = indicators;
        _config = settings.Value.IndexRegimeFilter;
    }

    public Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default)
    {
        // IndexRegimeFilter is used as a global filter, not as a standalone signal
        // MarketRegime is computed separately and passed to all detectors
        return Task.FromResult<PatternSignal?>(null);
    }
}
