using StockTrader.Configuration;

namespace StockTrader.Services.Notification;

/// <summary>
/// 런타임 알림 설정을 제공하는 포트.
/// DB의 UserSettings가 appsettings.json보다 우선 적용된다 (DB 값이 null이면 appsettings fallback).
/// Singleton 채널 구현체들이 이 서비스를 통해 항상 최신 설정을 조회한다.
/// </summary>
public interface INotificationSettingsProvider
{
    /// <summary>
    /// 현재 유효한 NotificationSettings를 반환한다.
    /// DB UserSettings에 알림 필드가 설정되어 있으면 해당 값이 appsettings 값을 덮어쓴다.
    /// </summary>
    Task<NotificationSettings> GetEffectiveSettingsAsync(CancellationToken ct = default);
}
