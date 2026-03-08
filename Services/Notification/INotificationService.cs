using StockTrader.Models;

namespace StockTrader.Services.Notification;

public interface INotificationService
{
    event Action<TradeRecommendation>? OnNewRecommendation;
    event Action<string>? OnAlert;
    event Action<PriceUpdate>? OnPriceUpdate;
    event Action<string>? OnBarUpdate;
    event Action<bool>? OnStreamingStatusChanged;

    void Notify(TradeRecommendation recommendation);
    void Alert(string message);
    void PublishPriceUpdate(PriceUpdate update);
    void PublishBarUpdate(string symbol);
    void PublishStreamingStatus(bool isActive);
}
