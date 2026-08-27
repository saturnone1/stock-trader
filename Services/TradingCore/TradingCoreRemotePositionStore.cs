using StockTrader.Application.Trading;
using StockTrader.Application.TradingCore;
using StockTrader.Models;

namespace StockTrader.Services.TradingCore;

/// <summary>Read-only compatibility port over the canonical Trading Core portfolio.</summary>
internal sealed class TradingCoreRemotePositionStore(ITradingCoreControlPlane core)
    : IOpenPositionStore
{
    public async Task<List<Position>> GetOpenPositionsAsync(CancellationToken ct = default) =>
        (await core.GetPortfolioAsync(ct)).Positions
            .Where(value => value.ClosedAtUtc is null)
            .Select(TradingCoreProjectionMapper.Position)
            .OrderByDescending(value => value.OpenedAt)
            .ToList();

    public Task SavePositionAsync(Position position, CancellationToken ct = default) =>
        throw new InvalidOperationException("remote-trading-core-position-store-is-read-only");
}
