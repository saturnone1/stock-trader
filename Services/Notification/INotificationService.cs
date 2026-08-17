using StockTrader.Models;

namespace StockTrader.Services.Notification;

public interface INotificationService
{
    void Notify(TradeRecommendation recommendation);
    void Alert(string message);
    void PublishPriceUpdate(PriceUpdate update);
    void PublishBarUpdate(string symbol);
    void PublishStreamingStatus(bool isActive);
}
