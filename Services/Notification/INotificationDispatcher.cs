using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// 모든 활성 채널에 알림을 병렬로 발송하는 디스패처 계약.
/// 개별 채널 실패는 격리되어 다른 채널에 영향을 주지 않는다.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default);
    Task DispatchAlertAsync(string message, CancellationToken ct = default);
    Task<Dictionary<string, bool>> TestAllChannelsAsync(CancellationToken ct = default);
    Task<bool> TestChannelAsync(string channelName, CancellationToken ct = default);
}
