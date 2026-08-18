namespace StockTrader.Application.Risk;

public sealed record RiskStateSnapshot(
    decimal DailyPnL,
    decimal DailyPnLPercent,
    bool IsTradingHalted,
    int OpenPositionCount,
    IReadOnlyDictionary<string, int> PositionsPerSector,
    DateTime LastUpdated);

public sealed record RiskManagementOptions(
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

public sealed record RiskOpenPosition(
    int AccountId,
    string Symbol,
    string? Sector,
    decimal UnrealizedPnl);

public sealed record RiskAccountBalance(
    decimal TotalEquity,
    decimal DailyPnl);

public sealed record RiskAccountEvidence(
    int AccountId,
    RiskAccountBalance? Balance);

public sealed record RiskPortfolioEvidence(
    decimal DefaultAccountSize,
    IReadOnlyList<RiskOpenPosition> OpenPositions,
    IReadOnlyList<RiskAccountEvidence> EnabledAccounts);

public interface IRiskManagementDataSource
{
    Task<int?> GetActiveAccountIdAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RiskOpenPosition>> GetOpenPositionsAsync(
        CancellationToken ct = default);

    Task<RiskPortfolioEvidence> LoadPortfolioEvidenceAsync(
        CancellationToken ct = default);
}

public interface IRiskManagementService
{
    Task<RiskStateSnapshot> GetCurrentRiskStateAsync(
        CancellationToken ct = default);

    Task<(bool Allowed, string Reason)> CanOpenPositionAsync(
        string symbol,
        string sector,
        CancellationToken ct = default);

    decimal CalculatePositionSize(
        decimal accountSize,
        decimal riskPercent,
        decimal entryPrice,
        decimal stopLossPrice);

    Task UpdateDailyPnLAsync(CancellationToken ct = default);
}
