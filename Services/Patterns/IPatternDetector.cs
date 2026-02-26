using StockTrader.Models;

namespace StockTrader.Services.Patterns;

public interface IPatternDetector
{
    PatternType PatternType { get; }
    Task<PatternSignal?> DetectAsync(string symbol, OhlcvBar[] bars,
        MarketRegime regime, CancellationToken ct = default);
}
