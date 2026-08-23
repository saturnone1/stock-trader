namespace StockTrader.ServiceContracts.Optimization;

using System.Text.Json;

public sealed record OptimizationBar(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Vwap);

public sealed record OptimizationPreparedSeries(
    string Symbol,
    string TimeFrame,
    IReadOnlyList<OptimizationBar> Bars,
    IReadOnlyList<decimal> Atr,
    IReadOnlyList<decimal> Closes,
    IReadOnlyList<decimal> TqqqProtectiveStopFloor,
    IReadOnlyList<decimal> CumulativeRsi2,
    IReadOnlyList<decimal> CumulativeRsi2TrendMa);

public sealed record OptimizationRegimeSnapshot(
    DateOnly Date,
    bool BenchmarkAboveLongAverage,
    decimal BenchmarkPrice,
    decimal BenchmarkLongAverage,
    decimal VolatilityLevel,
    string Label,
    DateTime AsOf,
    int MlClusterId,
    string MlLabel);

public sealed record OptimizationRiskSnapshot(
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector);

public sealed record OptimizationPreparedDataSet(
    int ContractVersion,
    string DataHash,
    IReadOnlyList<OptimizationPreparedSeries> Series,
    IReadOnlyList<OptimizationRegimeSnapshot> Regimes,
    OptimizationRiskSnapshot Risk)
{
    public string PatternSettingsJson { get; init; } = "{}";
}

public static class OptimizationPreparedDataIdentity
{
    public static string Compute(
        IReadOnlyList<OptimizationPreparedSeries> series,
        IReadOnlyList<OptimizationRegimeSnapshot> regimes,
        OptimizationRiskSnapshot risk,
        string patternSettingsJson = "{}") => CanonicalJsonHash.Compute(new
        {
            Series = series,
            Regimes = regimes,
            Risk = risk,
            PatternSettingsJson = patternSettingsJson
        });
}

public static class OptimizationPreparedDataCompatibilityPolicy
{
    public static string? Error(OptimizationPreparedDataSet data)
    {
        if (data.ContractVersion != OptimizationWorkerContractCatalog.EvaluationInputVersion)
            return "unsupported-prepared-data-contract";
        if (data.Series.Count == 0)
            return "empty-prepared-data";
        if (data.Series.GroupBy(item => $"{item.TimeFrame}|{item.Symbol}",
                StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return "duplicate-prepared-series";
        if (data.Series.Any(HasInvalidShape))
            return "invalid-prepared-series-shape";
        if (!IsJsonObject(data.PatternSettingsJson))
            return "invalid-pattern-settings";

        var expected = OptimizationPreparedDataIdentity.Compute(
            data.Series, data.Regimes, data.Risk, data.PatternSettingsJson);
        return string.Equals(expected, data.DataHash, StringComparison.Ordinal)
            ? null
            : "prepared-data-hash-mismatch";
    }

    private static bool HasInvalidShape(OptimizationPreparedSeries series)
    {
        var count = series.Bars.Count;
        return string.IsNullOrWhiteSpace(series.Symbol)
            || string.IsNullOrWhiteSpace(series.TimeFrame)
            || count == 0
            || series.Atr.Count != count
            || series.Closes.Count != count
            || series.TqqqProtectiveStopFloor.Count != count
            || series.CumulativeRsi2.Count != count
            || series.CumulativeRsi2TrendMa.Count != count;
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
