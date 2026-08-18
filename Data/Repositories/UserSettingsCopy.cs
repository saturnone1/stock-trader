using StockTrader.Models;

namespace StockTrader.Data.Repositories;

internal static class UserSettingsCopy
{
    public static UserSettings Create(UserSettings value) => new()
    {
        Id = value.Id,
        OrderMode = value.OrderMode,
        PreferredDataSource = value.PreferredDataSource,
        EnabledPatterns = value.EnabledPatterns.ToList(),
        WatchlistSymbols = value.WatchlistSymbols.ToList(),
        SoundAlerts = value.SoundAlerts,
        LastModified = value.LastModified,
        AccountSize = value.AccountSize,
        RiskPerTradePercent = value.RiskPerTradePercent,
        DailyLossLimitPercent = value.DailyLossLimitPercent,
        MaxTotalPositions = value.MaxTotalPositions,
        MaxPositionsPerSector = value.MaxPositionsPerSector,
        MinExpectancy = value.MinExpectancy,
        LiveParameterOverridesJson = value.LiveParameterOverridesJson,
        EnableTelegram = value.EnableTelegram,
        TelegramBotToken = value.TelegramBotToken,
        TelegramChatId = value.TelegramChatId,
        EnableDiscord = value.EnableDiscord,
        DiscordWebhookUrl = value.DiscordWebhookUrl,
        EnableEmail = value.EnableEmail,
        SmtpHost = value.SmtpHost,
        SmtpPort = value.SmtpPort,
        SmtpUseSsl = value.SmtpUseSsl,
        SmtpUsername = value.SmtpUsername,
        SmtpPassword = value.SmtpPassword,
        EmailFrom = value.EmailFrom,
        EmailTo = value.EmailTo,
        DailyReportTimeKst = value.DailyReportTimeKst,
        Tqqq200SmaAllowedSymbols = value.Tqqq200SmaAllowedSymbols
    };
}
