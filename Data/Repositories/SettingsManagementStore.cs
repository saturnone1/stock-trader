using StockTrader.Application.Settings;
using StockTrader.Models;

namespace StockTrader.Data.Repositories;

/// <summary>EF 설정 엔티티를 애플리케이션 설정 계약으로 변환하는 SQLite 어댑터입니다.</summary>
public sealed class SettingsManagementStore(ISettingsRepository repository)
    : ISettingsManagementStore
{
    public async Task<ManagedSettings> GetAsync(CancellationToken ct = default) =>
        ToSnapshot(await repository.GetAsync(ct));

    public async Task SaveAsync(ManagedSettings settings, CancellationToken ct = default)
    {
        var entity = await repository.GetAsync(ct);
        Apply(settings, entity);
        await repository.SaveAsync(entity, ct);
    }

    private static ManagedSettings ToSnapshot(UserSettings value) => new()
    {
        Id = value.Id,
        OrderMode = value.OrderMode,
        PreferredDataSource = value.PreferredDataSource,
        EnabledPatterns = value.EnabledPatterns.ToArray(),
        WatchlistSymbols = value.WatchlistSymbols.ToArray(),
        SoundAlerts = value.SoundAlerts,
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
        Tqqq200SmaAllowedSymbols = value.Tqqq200SmaAllowedSymbols,
        LastModified = value.LastModified
    };

    private static void Apply(ManagedSettings source, UserSettings target)
    {
        target.OrderMode = source.OrderMode;
        target.PreferredDataSource = source.PreferredDataSource;
        target.EnabledPatterns = source.EnabledPatterns.ToList();
        target.WatchlistSymbols = source.WatchlistSymbols.ToList();
        target.SoundAlerts = source.SoundAlerts;
        target.AccountSize = source.AccountSize;
        target.RiskPerTradePercent = source.RiskPerTradePercent;
        target.DailyLossLimitPercent = source.DailyLossLimitPercent;
        target.MaxTotalPositions = source.MaxTotalPositions;
        target.MaxPositionsPerSector = source.MaxPositionsPerSector;
        target.MinExpectancy = source.MinExpectancy;
        target.LiveParameterOverridesJson = source.LiveParameterOverridesJson;
        target.EnableTelegram = source.EnableTelegram;
        target.TelegramBotToken = source.TelegramBotToken;
        target.TelegramChatId = source.TelegramChatId;
        target.EnableDiscord = source.EnableDiscord;
        target.DiscordWebhookUrl = source.DiscordWebhookUrl;
        target.EnableEmail = source.EnableEmail;
        target.SmtpHost = source.SmtpHost;
        target.SmtpPort = source.SmtpPort;
        target.SmtpUseSsl = source.SmtpUseSsl;
        target.SmtpUsername = source.SmtpUsername;
        target.SmtpPassword = source.SmtpPassword;
        target.EmailFrom = source.EmailFrom;
        target.EmailTo = source.EmailTo;
        target.DailyReportTimeKst = source.DailyReportTimeKst;
        target.Tqqq200SmaAllowedSymbols = source.Tqqq200SmaAllowedSymbols;
        target.LastModified = source.LastModified;
    }
}
