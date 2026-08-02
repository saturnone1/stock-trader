using StockTrader.Models;

namespace StockTrader.Services.Risk;

public interface IRiskManagementService
{
    Task<RiskState> GetCurrentRiskStateAsync(CancellationToken ct = default);
    Task<(bool Allowed, string Reason)> CanOpenPositionAsync(
        string symbol, string sector, CancellationToken ct = default);
    decimal CalculatePositionSize(decimal accountSize, decimal riskPercent,
        decimal entryPrice, decimal stopLossPrice);
    Task UpdateDailyPnLAsync(CancellationToken ct = default);
}
