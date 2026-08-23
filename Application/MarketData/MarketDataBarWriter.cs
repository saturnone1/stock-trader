using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Application.MarketData;

public sealed record MarketDataBarWrite(
    DataSource Provider,
    IReadOnlyList<OhlcvBar> Bars,
    DateTime? RequestedFromUtc = null,
    DateTime? RequestedToUtc = null,
    bool IsComplete = false,
    string? RequestId = null);

public interface IMarketDataBarWriter
{
    Task WriteAsync(MarketDataBarWrite write, CancellationToken ct = default);
}
