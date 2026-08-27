using System.Collections.Frozen;
using StockTrader.Application.Execution;
using StockTrader.Application.Risk;
using StockTrader.Application.TradingCore;
using StockTrader.ServiceContracts.TradingCore;

namespace StockTrader.Services.TradingCore;

/// <summary>
/// Presents canonical remote risk state to research callers. Entry admission remains advisory here;
/// Trading Core performs the only authoritative, broker-fresh final risk gate.
/// </summary>
internal sealed class TradingCoreRemoteRiskService(ITradingCoreControlPlane core)
    : IRiskManagementService
{
    public async Task<RiskStateSnapshot> GetCurrentRiskStateAsync(
        CancellationToken ct = default)
    {
        var portfolio = await RequireRemoteAsync(ct);
        var sectors = portfolio.Positions
            .Where(value => value.ClosedAtUtc is null && !string.IsNullOrWhiteSpace(value.Sector))
            .GroupBy(value => value.Sector, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(group => group.Key, group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        return new RiskStateSnapshot(
            portfolio.Risk.DailyPnL,
            portfolio.Risk.DailyPnLPercent,
            portfolio.Risk.IsTradingHalted,
            portfolio.Risk.OpenPositionCount,
            sectors,
            portfolio.Risk.ObservedAtUtc);
    }

    public async Task<(bool Allowed, string Reason)> CanOpenPositionAsync(
        string symbol,
        string sector,
        CancellationToken ct = default)
    {
        var portfolio = await RequireRemoteAsync(ct);
        return portfolio.Risk.IsTradingHalted
            ? (false, "Trading Core halted entries after its authoritative daily-loss check")
            : (true, "Trading Core performs the authoritative broker-fresh entry risk gate");
    }

    public decimal CalculatePositionSize(
        decimal accountSize,
        decimal riskPercent,
        decimal entryPrice,
        decimal stopLossPrice) => LongPositionSizingPolicy.CalculateRiskCapital(
            accountSize, riskPercent, entryPrice, stopLossPrice);

    public async Task UpdateDailyPnLAsync(CancellationToken ct = default) =>
        _ = await RequireRemoteAsync(ct);

    private async Task<TradingCorePortfolioView> RequireRemoteAsync(CancellationToken ct)
    {
        var status = await core.GetStatusAsync(ct);
        if (!status.Ready || status.Mode != TradingAuthorityMode.Remote)
            throw new InvalidOperationException("trading-core-remote-risk-authority-unavailable");
        return await core.GetPortfolioAsync(ct);
    }
}
