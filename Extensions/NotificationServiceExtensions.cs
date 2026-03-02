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

        // Notification Channels
        services.AddSingleton<INotificationChannel, TelegramNotificationChannel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("Telegram");
            var settings = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger = sp.GetRequiredService<ILogger<TelegramNotificationChannel>>();
            return new TelegramNotificationChannel(http, settings, logger);
        });
        services.AddSingleton<INotificationChannel, DiscordNotificationChannel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("Discord");
            var settings = sp.GetRequiredService<IOptions<NotificationSettings>>();
            var logger = sp.GetRequiredService<ILogger<DiscordNotificationChannel>>();
            return new DiscordNotificationChannel(http, settings, logger);
        });
        services.AddSingleton<INotificationChannel, EmailNotificationChannel>();

        // NotificationDispatcher: 모든 채널에 병렬 발송
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        // In-app notification (singleton for cross-component events)
        services.AddSingleton<INotificationService, InAppNotificationService>();

        // Streaming status (singleton for cross-service coordination)
        services.AddSingleton<IStreamingStatusService, StreamingStatusService>();

        return services;
    }
}
