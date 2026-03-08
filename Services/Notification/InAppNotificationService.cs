using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// In-App 알림 서비스.
/// Blazor 컴포넌트가 구독하는 이벤트를 발행하는 동시에,
/// NotificationDispatcher를 통해 외부 채널(Telegram, Discord, Email)에도 알림을 발송한다.
/// </summary>
public class InAppNotificationService : INotificationService
{
    private readonly ILogger<InAppNotificationService> _logger;
    private readonly INotificationDispatcher _dispatcher;

    public event Action<TradeRecommendation>? OnNewRecommendation;
    public event Action<string>? OnAlert;
    public event Action<PriceUpdate>? OnPriceUpdate;
    public event Action<string>? OnBarUpdate;
    public event Action<bool>? OnStreamingStatusChanged;

    public InAppNotificationService(
        ILogger<InAppNotificationService> logger,
        INotificationDispatcher dispatcher)
    {
        _logger = logger;
        _dispatcher = dispatcher;
    }

    public void Notify(TradeRecommendation recommendation)
    {
        _logger.LogInformation("New recommendation: {Pattern} {Symbol} @ {Price}",
            recommendation.PatternType, recommendation.Symbol, recommendation.EntryPrice);

        OnNewRecommendation?.Invoke(recommendation);

        // 외부 채널에 비동기로 발송 — fire-and-forget (실패해도 In-App 알림에 영향 없음)
        _ = DispatchSignalSafeAsync(recommendation);
    }

    public void Alert(string message)
    {
        _logger.LogInformation("Alert: {Message}", message);
        OnAlert?.Invoke(message);

        _ = DispatchAlertSafeAsync(message);
    }

    public void PublishPriceUpdate(PriceUpdate update)
    {
        _logger.LogDebug("Price update: {Symbol} @ {Price}", update.Symbol, update.Price);
        OnPriceUpdate?.Invoke(update);
        // 가격 업데이트는 외부 채널 발송 대상 아님 (과도한 트래픽 방지)
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
