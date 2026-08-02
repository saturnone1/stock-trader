using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Data.Repositories;

namespace StockTrader.Services.Notification;

/// <summary>
/// DB의 UserSettings에서 알림 설정을 읽어 appsettings.json과 병합하는 구현체.
/// DB 값(non-null)이 appsettings 값보다 우선 적용된다.
/// </summary>
public sealed class DbNotificationSettingsProvider : INotificationSettingsProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationSettings _appSettings;
    private readonly ILogger<DbNotificationSettingsProvider> _logger;

    public DbNotificationSettingsProvider(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationSettings> appSettings,
        ILogger<DbNotificationSettingsProvider> logger)
    {
        _scopeFactory = scopeFactory;
        _appSettings  = appSettings.Value;
        _logger       = logger;
    }

    public async Task<NotificationSettings> GetEffectiveSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
            var userSettings = await repo.GetAsync(ct);

            // appsettings 값을 기반으로 복사본 생성 후 DB 값으로 오버라이드
            return new NotificationSettings
            {
                // ── Telegram ──────────────────────────────────────────
                EnableTelegram   = userSettings.EnableTelegram   ?? _appSettings.EnableTelegram,
                TelegramBotToken = !string.IsNullOrWhiteSpace(userSettings.TelegramBotToken)
                                    ? userSettings.TelegramBotToken
                                    : _appSettings.TelegramBotToken,
                TelegramChatId   = !string.IsNullOrWhiteSpace(userSettings.TelegramChatId)
                                    ? userSettings.TelegramChatId
                                    : _appSettings.TelegramChatId,

                // ── Discord ───────────────────────────────────────────
                EnableDiscord    = userSettings.EnableDiscord    ?? _appSettings.EnableDiscord,
                DiscordWebhookUrl = !string.IsNullOrWhiteSpace(userSettings.DiscordWebhookUrl)
                                    ? userSettings.DiscordWebhookUrl
                                    : _appSettings.DiscordWebhookUrl,

                // ── Email (SMTP) ──────────────────────────────────────
                EnableEmail   = userSettings.EnableEmail   ?? _appSettings.EnableEmail,
                SmtpHost      = !string.IsNullOrWhiteSpace(userSettings.SmtpHost)
                                 ? userSettings.SmtpHost
                                 : _appSettings.SmtpHost,
                SmtpPort      = userSettings.SmtpPort      ?? _appSettings.SmtpPort,
                SmtpUseSsl    = userSettings.SmtpUseSsl    ?? _appSettings.SmtpUseSsl,
                SmtpUsername  = !string.IsNullOrWhiteSpace(userSettings.SmtpUsername)
                                 ? userSettings.SmtpUsername
                                 : _appSettings.SmtpUsername,
                SmtpPassword  = !string.IsNullOrWhiteSpace(userSettings.SmtpPassword)
                                 ? userSettings.SmtpPassword
                                 : _appSettings.SmtpPassword,
                EmailFrom     = !string.IsNullOrWhiteSpace(userSettings.EmailFrom)
                                 ? userSettings.EmailFrom
                                 : _appSettings.EmailFrom,
                EmailTo       = !string.IsNullOrWhiteSpace(userSettings.EmailTo)
                                 ? userSettings.EmailTo
                                 : _appSettings.EmailTo,

                // ── Daily Report ──────────────────────────────────────
                // DailyReportTime은 DailyReportService에서 별도 KST→ET 변환 후 사용.
                // 여기서는 appsettings 기본값(ET 기준)을 그대로 유지.
                DailyReportTime = _appSettings.DailyReportTime,

                // ── Retry (항상 appsettings 기준) ─────────────────────
                MaxRetryAttempts  = _appSettings.MaxRetryAttempts,
                RetryDelaySeconds = _appSettings.RetryDelaySeconds,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DB 알림 설정 조회 실패 — appsettings.json 기본값으로 fallback");
            return _appSettings;
        }
    }
}
