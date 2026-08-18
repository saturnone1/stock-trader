namespace StockTrader.Application.SymbolProfiles;

/// <summary>종목별 전략 배정 유스케이스가 사용하는 저장소 독립 스냅샷입니다.</summary>
public sealed record ManagedSymbolProfile
{
    public long Id { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<PatternType> EnabledPatterns { get; init; } = [];
    public string? ParameterOverridesJson { get; init; }
    public string? WeightStrategyJson { get; init; }
    public decimal RiskPerTradePercent { get; init; }
    public int MaxTotalPositions { get; init; }
    public decimal? BacktestReturnPct { get; init; }
    public decimal? BacktestWinRate { get; init; }
    public decimal? BacktestMaxDrawdown { get; init; }
    public decimal? BacktestSharpe { get; init; }
    public int? BacktestTrades { get; init; }
    public DateTime? BacktestFrom { get; init; }
    public DateTime? BacktestTo { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record SymbolProfileUpsertCommand
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
}

public sealed record SymbolProfileUpsertOutcome(
    ManagedSymbolProfile? Profile,
    bool Created,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Profile is not null && Errors.Count == 0;
}
