using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Services.Patterns;

public class PatternDetectionService
{
    private readonly IEnumerable<IPatternDetector> _detectors;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<PatternDetectionService> _logger;

    public PatternDetectionService(
        IEnumerable<IPatternDetector> detectors,
        ISettingsRepository settingsRepo,
        ILogger<PatternDetectionService> logger)
    {
        _detectors = detectors;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<List<PatternSignal>> ScanSymbolAsync(
        string symbol, OhlcvBar[] bars, MarketRegime regime, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetAsync(ct);
        var signals = new List<PatternSignal>();

        foreach (var detector in _detectors
            .Where(d => settings.EnabledPatterns.Contains(d.PatternType)))
        {
            try
            {
                var signal = await detector.DetectAsync(symbol, bars, regime, ct);
                if (signal != null)
                {
                    _logger.LogInformation("Pattern {Pattern} detected for {Symbol}",
                        detector.PatternType, symbol);
                    signals.Add(signal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting pattern {Pattern} for {Symbol}",
                    detector.PatternType, symbol);
            }
        }

        return signals;
    }
}
