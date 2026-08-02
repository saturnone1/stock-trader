namespace StockTrader.Configuration;

public class NotificationSettings
{
    // ── Telegram ──────────────────────────────────────────────────────
    public bool EnableTelegram { get; set; } = false;
    public string TelegramBotToken { get; set; } = string.Empty;
    public string TelegramChatId { get; set; } = string.Empty;

    // ── Discord ───────────────────────────────────────────────────────
    public bool EnableDiscord { get; set; } = false;
    public string DiscordWebhookUrl { get; set; } = string.Empty;

    // ── Email (SMTP) ──────────────────────────────────────────────────
    public bool EnableEmail { get; set; } = false;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string EmailFrom { get; set; } = string.Empty;
    public string EmailTo { get; set; } = string.Empty;

    // ── Daily Report ─────────────────────────────────────────────────
    /// <summary>일일 요약 리포트 발송 시간 (HH:mm, ET 기준). 예: "16:30"</summary>
    public string DailyReportTime { get; set; } = "16:30";

    // ── Retry ─────────────────────────────────────────────────────────
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
}
