namespace StockTrader.Services.Streaming;

public interface IStreamingStatusService
{
    bool IsStreamingActive { get; }
    bool IsReconnecting { get; }
    DateTime? LastBarReceivedUtc { get; }
    void MarkActive(DateTime receivedUtc);
    void MarkInactive();
    void MarkReconnecting();
}
