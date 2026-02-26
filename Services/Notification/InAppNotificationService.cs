using StockTrader.Models;

namespace StockTrader.Services.Notification;

public class InAppNotificationService : INotificationService
{
    private readonly ILogger<InAppNotificationService> _logger;

    public event Action<TradeRecommendation>? OnNewRecommendation;
    public event Action<string>? OnAlert;
    public event Action<PriceUpdate>? OnPriceUpdate;
    public event Action<string>? OnBarUpdate;
    public event Action<bool>? OnStreamingStatusChanged;

    public InAppNotificationService(ILogger<InAppNotificationService> logger)
    {
        _logger = logger;
    }

    public void Notify(TradeRecommendation recommendation)
    {
        _logger.LogInformation("New recommendation: {Pattern} {Symbol} @ {Price}",
            recommendation.PatternType, recommendation.Symbol, recommendation.EntryPrice);
        OnNewRecommendation?.Invoke(recommendation);
    }

    public void Alert(string message)
    {
        _logger.LogInformation("Alert: {Message}", message);
        OnAlert?.Invoke(message);
    }

    public void PublishPriceUpdate(PriceUpdate update)
    {
        _logger.LogDebug("Price update: {Symbol} @ {Price}", update.Symbol, update.Price);
        OnPriceUpdate?.Invoke(update);
    }

    public void PublishBarUpdate(string symbol)
    {
        _logger.LogDebug("Bar update: {Symbol}", symbol);
        OnBarUpdate?.Invoke(symbol);
    }

    public void PublishStreamingStatus(bool isActive)
    {
        _logger.LogInformation("Streaming status changed: {Status}", isActive ? "Active" : "Inactive");
        OnStreamingStatusChanged?.Invoke(isActive);
    }
}
