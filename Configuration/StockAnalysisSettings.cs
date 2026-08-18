namespace StockTrader.Configuration;

public sealed class StockAnalysisSettings
{
    public int MaxParallelAnalyses { get; set; } = 3;
    public int AnalysisCacheSeconds { get; set; } = 25;
    public int RegimeCacheMinutes { get; set; } = 5;
    public int StatisticsCacheMinutes { get; set; } = 10;
    public int HistoryLookbackDays { get; set; } = 365;
    public int MinimumHistoryBars { get; set; } = 50;
    public int RegimeLookbackDays { get; set; } = 400;
}
