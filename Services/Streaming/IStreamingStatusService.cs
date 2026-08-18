using StockTrader.Application.MarketData;

namespace StockTrader.Services.Streaming;

public interface IStreamingStatusService : IRealtimeMarketDataStatus
{
    bool IsStreamingActive { get; }
    /// <summary>True while the service is attempting to reconnect after an unexpected disconnect.</summary>
    bool IsReconnecting { get; }
    DateTime? LastBarReceivedUtc { get; }
    void MarkConnected();
    void MarkActive();
    void MarkInactive();
    void MarkReconnecting();
}
