using StockTrader.Application.SymbolProfiles;

namespace StockTrader.Api.Contracts;

public sealed record SymbolProfileResponse(
    long Id,
    string Symbol,
    string Name,
    bool IsActive,
    IReadOnlyList<PatternType> EnabledPatterns,
    string? ParameterOverridesJson,
    string? WeightStrategyJson,
    decimal RiskPerTradePercent,
    int MaxTotalPositions,
    decimal? BacktestReturnPct,
    decimal? BacktestWinRate,
    decimal? BacktestMaxDrawdown,
    decimal? BacktestSharpe,
    int? BacktestTrades,
    string? BacktestFrom,
    string? BacktestTo,
    string CreatedAt,
    string UpdatedAt)
{
    public static SymbolProfileResponse Create(ManagedSymbolProfile value) => new(
        value.Id,
        value.Symbol,
        value.Name,
        value.IsActive,
        value.EnabledPatterns,
        value.ParameterOverridesJson,
        value.WeightStrategyJson,
        value.RiskPerTradePercent,
        value.MaxTotalPositions,
        value.BacktestReturnPct,
        value.BacktestWinRate,
        value.BacktestMaxDrawdown,
        value.BacktestSharpe,
        value.BacktestTrades,
        value.BacktestFrom?.ToString("yyyy-MM-dd"),
        value.BacktestTo?.ToString("yyyy-MM-dd"),
        value.CreatedAt.ToString("o"),
        value.UpdatedAt.ToString("o"));
}

public sealed record SymbolProfileUpsertRequest
{
    public required string Symbol { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<PatternType>? EnabledPatterns { get; init; }
    public string? ParameterOverridesJson { get; init; }
    public string? WeightStrategyJson { get; init; }
    public decimal? RiskPerTradePercent { get; init; }
    public int? MaxTotalPositions { get; init; }
    public decimal? BacktestReturnPct { get; init; }
    public decimal? BacktestWinRate { get; init; }
    public decimal? BacktestMaxDrawdown { get; init; }
    public decimal? BacktestSharpe { get; init; }
    public int? BacktestTrades { get; init; }
    public DateTime? BacktestFrom { get; init; }
    public DateTime? BacktestTo { get; init; }

    public SymbolProfileUpsertCommand ToCommand() => new()
    {
        Symbol = Symbol,
        Name = Name,
        EnabledPatterns = EnabledPatterns,
        ParameterOverridesJson = ParameterOverridesJson,
        WeightStrategyJson = WeightStrategyJson,
        RiskPerTradePercent = RiskPerTradePercent,
        MaxTotalPositions = MaxTotalPositions,
        BacktestReturnPct = BacktestReturnPct,
        BacktestWinRate = BacktestWinRate,
        BacktestMaxDrawdown = BacktestMaxDrawdown,
        BacktestSharpe = BacktestSharpe,
        BacktestTrades = BacktestTrades,
        BacktestFrom = BacktestFrom,
        BacktestTo = BacktestTo
    };
}

public sealed record SymbolProfileActionResponse(string Message);
public sealed record SymbolProfileErrorResponse(IReadOnlyList<string> Errors);
