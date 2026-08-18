using Microsoft.Extensions.Options;
using StockTrader.Application.Reporting;
using StockTrader.Application.MarketData;
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
            var timeProvider     = sp.GetRequiredService<TimeProvider>();
            return new DiscordNotificationChannel(
                http, settingsProvider, fallback, logger, timeProvider);
        });
        services.AddSingleton<INotificationChannel, EmailNotificationChannel>(sp =>
        {
            var settingsProvider = sp.GetRequiredService<INotificationSettingsProvider>();
            var fallback         = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger           = sp.GetRequiredService<ILogger<EmailNotificationChannel>>();
            var timeProvider     = sp.GetRequiredService<TimeProvider>();
            return new EmailNotificationChannel(
                settingsProvider, fallback, logger, timeProvider);
        });

        // NotificationDispatcher: 모든 채널에 병렬 발송 (DB 설정 우선)
        services.AddSingleton<NotificationDispatcher>();
        services.AddSingleton<INotificationDispatcher>(sp =>
            sp.GetRequiredService<NotificationDispatcher>());
        services.AddSingleton<IDailyReportPublisher>(sp =>
            sp.GetRequiredService<NotificationDispatcher>());

        // Backend notification facade and external dispatch coordinator
        services.AddSingleton<INotificationService, BackendNotificationService>();

        // Streaming status (singleton for cross-service coordination)
        services.AddSingleton<StreamingStatusService>();
        services.AddSingleton<IStreamingStatusService>(serviceProvider =>
            serviceProvider.GetRequiredService<StreamingStatusService>());
        services.AddSingleton<IRealtimeMarketDataStatus>(serviceProvider =>
            serviceProvider.GetRequiredService<StreamingStatusService>());

        return services;
    }
}
