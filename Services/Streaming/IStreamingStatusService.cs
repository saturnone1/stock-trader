namespace StockTrader.Services.Streaming;

public interface IStreamingStatusService
{
    bool IsStreamingActive { get; }
    /// <summary>True while the service is attempting to reconnect after an unexpected disconnect.</summary>
    bool IsReconnecting { get; }
    DateTime? LastBarReceivedUtc { get; }
    void MarkActive(DateTime receivedUtc);
    void MarkInactive();
    void MarkReconnecting();
}
