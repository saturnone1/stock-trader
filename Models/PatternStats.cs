using StockTrader.Domain.Statistics;

namespace StockTrader.Models;

public class PatternStats
{
    public long Id { get; set; }
    public PatternType PatternType { get; set; }
    public string? Symbol { get; set; }
    public int SampleSize { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgWinPercent { get; set; }
    public decimal AvgLossPercent { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public DateTime LastUpdated { get; set; }

    public decimal Expectancy => PatternStatisticsMetricPolicy.CalculateExpectancy(
        WinRate,
        AvgWinPercent,
        AvgLossPercent);

    public decimal ProfitFactor => PatternStatisticsMetricPolicy.CalculateProfitFactor(
        WinRate,
        AvgWinPercent,
        AvgLossPercent);
}
