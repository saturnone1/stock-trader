using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// Backend notification facade. Signal and alert messages are logged and dispatched
/// to configured external channels; high-frequency market updates remain log-only.
/// </summary>
public class BackendNotificationService : INotificationService
{
    private readonly ILogger<BackendNotificationService> _logger;
    private readonly INotificationDispatcher _dispatcher;

    public BackendNotificationService(
        ILogger<BackendNotificationService> logger,
        INotificationDispatcher dispatcher)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public void Notify(TradeRecommendation recommendation)
    {
        _logger.LogInformation("New recommendation: {Pattern} {Symbol} @ {Price}",
            recommendation.PatternType, recommendation.Symbol, recommendation.EntryPrice);

        // External dispatch is isolated so a channel failure cannot stop signal processing.
        _ = DispatchSignalSafeAsync(recommendation);
    }

    public void Alert(string message)
    {
        _logger.LogInformation("Alert: {Message}", message);
        _ = DispatchAlertSafeAsync(message);
    }

    public void PublishPriceUpdate(PriceUpdate update)
    {
        _logger.LogDebug("Price update: {Symbol} @ {Price}", update.Symbol, update.Price);
        // Price updates are intentionally not sent to external channels.
    }

    public void PublishBarUpdate(string symbol)
    {
        _logger.LogDebug("Bar update: {Symbol}", symbol);
    }

    public void PublishStreamingStatus(bool isActive)
    {
        _logger.LogInformation("Streaming status changed: {Status}", isActive ? "Active" : "Inactive");
    }

    // ── Private fire-and-forget helpers ──────────────────────────────

    private async Task DispatchSignalSafeAsync(TradeRecommendation recommendation)
    {
        try
        {
            await _dispatcher.DispatchSignalAsync(recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "External notification dispatch failed for signal {Symbol}", recommendation.Symbol);
        }
    }

    private async Task DispatchAlertSafeAsync(string message)
    {
        try
        {
            await _dispatcher.DispatchAlertAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "External notification dispatch failed for alert");
        }
    }
}
