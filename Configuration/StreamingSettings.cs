namespace StockTrader.Configuration;

public sealed class StreamingSettings
{
    public int MaxReconnectAttempts { get; set; }
    public int InitialReconnectDelaySeconds { get; set; }
    public int MaxReconnectDelaySeconds { get; set; }
    public int StatusStalenessSeconds { get; set; }
    public int BarFlushIntervalSeconds { get; set; }
    public int WatchlistSyncIntervalSeconds { get; set; }
    public int BufferCapacity { get; set; }
}
