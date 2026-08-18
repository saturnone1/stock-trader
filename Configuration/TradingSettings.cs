namespace StockTrader.Configuration;

public class TradingSettings
{
    public const int MinimumEntryReconciliationIntervalSeconds = 5;
    public const int MaximumEntryReconciliationIntervalSeconds = 300;

    public decimal DefaultAccountSize { get; set; } = 100_000m;
    public decimal RiskPerTradePercent { get; set; } = 0.01m;
    public decimal DailyLossLimitPercent { get; set; } = 0.03m;
    public int MaxPositionsPerSector { get; set; } = 2;
    public int MaxTotalPositions { get; set; } = 7;
    public decimal MinExpectancy { get; set; } = 0m;
    public decimal MinConfidence { get; set; } = 0.3m;
    public int DataFetchIntervalSeconds { get; set; } = 60;
    public int RiskCheckIntervalSeconds { get; set; } = 30;
    public int RiskMonitorMaxConsecutiveFailures { get; set; } = 5;
    public int RiskMonitorCooldownSeconds { get; set; } = 300;
    public int RiskHaltAlertIntervalMinutes { get; set; } = 60;
    public int EntryReconciliationIntervalSeconds { get; set; } = 15;
    public int EntryReconciliationBatchSize { get; set; } = 100;
    public int PatternScanMaxRetries { get; set; } = 3;
    public int PatternScanMaxConsecutiveFailures { get; set; } = 5;
    public int PatternScanCooldownSeconds { get; set; } = 300;
    public int DailyDataSyncIntervalMinutes { get; set; } = 30;
    public int DailyDataSyncCloseDelayMinutes { get; set; } = 60;
    public int DailyDataSyncMaxRetries { get; set; } = 3;
    public int DailyDataSyncMaxConsecutiveFailures { get; set; } = 5;
    public int DailyDataSyncCooldownSeconds { get; set; } = 300;
    public int PositionMonitoringIntervalSeconds { get; set; } = 60;
    public int PositionOrderResolutionMaxAttempts { get; set; } = 10;
    public int PositionOrderResolutionDelayMilliseconds { get; set; } = 500;
    public string MarketOpenET { get; set; } = "09:30:00";
    public string MarketCloseET { get; set; } = "16:00:00";
}
