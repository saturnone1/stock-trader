namespace StockTrader.Domain.Optimization;

public enum OptimizationRankMetric
{
    TotalReturn,
    SortinoRatio,
    SharpeRatio,
    CalmarRatio,
    ProfitFactor,
    WinRate,
    AnnualizedReturn
}

public sealed record OptimizationRankDescriptor(
    string Code,
    string DisplayName,
    OptimizationRankMetric Metric,
    bool IsDefault = false);

/// <summary>
/// 최적화 결과 정렬, 자동 반영, API 메타데이터가 공유하는 순위 기준의 단일 원천이다.
/// </summary>
public static class OptimizationRankingCatalog
{
    public const string TotalReturnCode = "totalReturn";
    public const string SortinoRatioCode = "sortinoRatio";
    public const string SharpeRatioCode = "sharpeRatio";
    public const string CalmarRatioCode = "calmarRatio";
    public const string ProfitFactorCode = "profitFactor";
    public const string WinRateCode = "winRate";
    public const string AnnualizedReturnCode = "annualizedReturn";
    public const string DefaultCode = SortinoRatioCode;

    public static IReadOnlyList<OptimizationRankDescriptor> All { get; } =
    [
        new(SortinoRatioCode, "소르티노 비율", OptimizationRankMetric.SortinoRatio, IsDefault: true),
        new(SharpeRatioCode, "샤프 비율", OptimizationRankMetric.SharpeRatio),
        new(TotalReturnCode, "총 수익률", OptimizationRankMetric.TotalReturn),
        new(CalmarRatioCode, "칼마 비율", OptimizationRankMetric.CalmarRatio),
        new(ProfitFactorCode, "프로핏 팩터", OptimizationRankMetric.ProfitFactor),
        new(WinRateCode, "승률", OptimizationRankMetric.WinRate),
        new(AnnualizedReturnCode, "연환산 수익률", OptimizationRankMetric.AnnualizedReturn)
    ];

    public static string Normalize(string? code)
    {
        var candidate = code?.Trim();
        if (string.Equals(candidate, "sharperation", StringComparison.OrdinalIgnoreCase))
            return SharpeRatioCode;

        return All.FirstOrDefault(item =>
                item.Code.Equals(candidate, StringComparison.OrdinalIgnoreCase))?.Code
            ?? DefaultCode;
    }

    public static OptimizationRankMetric MetricFor(string? code)
    {
        var normalized = Normalize(code);
        return All.Single(item => item.Code == normalized).Metric;
    }
}
