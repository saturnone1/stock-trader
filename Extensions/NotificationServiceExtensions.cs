using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Services.Notification;
using StockTrader.Services.Streaming;

namespace StockTrader.Extensions;

public static class NotificationServiceExtensions
{
    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        // HttpClient factory used by notification channels
        services.AddHttpClient("Telegram");
        services.AddHttpClient("Discord");

        // DB 우선 알림 설정 provider (Singleton — ISettingsRepository는 Scope 내에서 조회)
        services.AddSingleton<INotificationSettingsProvider, DbNotificationSettingsProvider>();

        // Notification Channels (INotificationSettingsProvider 주입)
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>(sp =>
        {
            var factory          = sp.GetRequiredService<IHttpClientFactory>();
            var http             = factory.CreateClient("Telegram");
            var settingsProvider = sp.GetRequiredService<INotificationSettingsProvider>();
            var fallback         = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger           = sp.GetRequiredService<ILogger<TelegramNotificationChannel>>();
            return new TelegramNotificationChannel(http, settingsProvider, fallback, logger);
        });
        services.AddSingleton<INotificationChannel, DiscordNotificationChannel>(sp =>
        {
            var factory          = sp.GetRequiredService<IHttpClientFactory>();
            var http             = factory.CreateClient("Discord");
            var settingsProvider = sp.GetRequiredService<INotificationSettingsProvider>();
            var fallback         = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger           = sp.GetRequiredService<ILogger<DiscordNotificationChannel>>();
            return new DiscordNotificationChannel(http, settingsProvider, fallback, logger);
        });
        services.AddSingleton<INotificationChannel, EmailNotificationChannel>(sp =>
        {
            var settingsProvider = sp.GetRequiredService<INotificationSettingsProvider>();
            var fallback         = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger           = sp.GetRequiredService<ILogger<EmailNotificationChannel>>();
            return new EmailNotificationChannel(settingsProvider, fallback, logger);
        });

        // NotificationDispatcher: 모든 채널에 병렬 발송 (DB 설정 우선)
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        // In-app notification (singleton for cross-component events)
        services.AddSingleton<INotificationService, InAppNotificationService>();

        // Streaming status (singleton for cross-service coordination)
        services.AddSingleton<IStreamingStatusService, StreamingStatusService>();

        return services;
    }
}
