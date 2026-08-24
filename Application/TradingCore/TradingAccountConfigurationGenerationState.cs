namespace StockTrader.Application.TradingCore;

public sealed class TradingAccountConfigurationGenerationState(TimeProvider clock)
{
    private readonly object _gate = new();
    private string _contentHash = string.Empty;
    private long _generation = Math.Max(1, clock.GetUtcNow().Ticks);

    public (long Generation, DateTime IssuedAtUtc) Resolve(string contentHash)
    {
        lock (_gate)
        {
            if (!string.Equals(_contentHash, contentHash, StringComparison.Ordinal))
            {
                _contentHash = contentHash;
                _generation = Math.Max(_generation + 1, clock.GetUtcNow().Ticks);
            }
            return (_generation, new DateTime(_generation, DateTimeKind.Utc));
        }
    }
}
