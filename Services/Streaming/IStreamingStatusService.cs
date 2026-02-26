namespace StockTrader.Services.Streaming;

public interface IStreamingStatusService
{
    bool IsStreamingActive { get; }
    DateTime? LastBarReceivedUtc { get; }
    void MarkActive(DateTime receivedUtc);
    void MarkInactive();
}
