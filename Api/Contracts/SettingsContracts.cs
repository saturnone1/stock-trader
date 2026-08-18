using StockTrader.Application.Settings;

namespace StockTrader.Api.Contracts;

public sealed record SettingsOptionResponse(
    string Code,
    string DisplayName,
    string? Description = null);

public sealed record SettingsResponse(
    long Id,
    OrderMode OrderMode,
    DataSource PreferredDataSource,
    IReadOnlyList<PatternType> EnabledPatterns,
    IReadOnlyList<string> WatchlistSymbols,
    bool SoundAlerts,
    decimal AccountSize,
    decimal RiskPerTradePercent,
    decimal DailyLossLimitPercent,
    int MaxTotalPositions,
    int MaxPositionsPerSector,
    decimal MinExpectancy,
    string? LiveParameterOverridesJson,
    bool? EnableTelegram,
    bool TelegramBotTokenConfigured,
    string? TelegramChatId,
    bool? EnableDiscord,
    bool DiscordWebhookConfigured,
    bool? EnableEmail,
    string? SmtpHost,
    int? SmtpPort,
    bool? SmtpUseSsl,
    string? SmtpUsername,
    bool SmtpPasswordConfigured,
    string? EmailFrom,
    string? EmailTo,
    string? DailyReportTimeKst,
    string? Tqqq200SmaAllowedSymbols,
    DateTime LastModified,
    IReadOnlyList<SettingsOptionResponse> OrderModes,
    IReadOnlyList<SettingsOptionResponse> DataProviders,
    IReadOnlyList<SettingsOptionResponse> Patterns)
{
    public static SettingsResponse Create(ManagedSettings value) => new(
        value.Id,
        value.OrderMode,
        value.PreferredDataSource,
        value.EnabledPatterns,
        value.WatchlistSymbols,
        value.SoundAlerts,
        value.AccountSize,
        value.RiskPerTradePercent,
        value.DailyLossLimitPercent,
        value.MaxTotalPositions,
        value.MaxPositionsPerSector,
        value.MinExpectancy,
        value.LiveParameterOverridesJson,
        value.EnableTelegram,
        !string.IsNullOrWhiteSpace(value.TelegramBotToken),
        value.TelegramChatId,
        value.EnableDiscord,
        !string.IsNullOrWhiteSpace(value.DiscordWebhookUrl),
        value.EnableEmail,
        value.SmtpHost,
        value.SmtpPort,
        value.SmtpUseSsl,
        value.SmtpUsername,
        !string.IsNullOrWhiteSpace(value.SmtpPassword),
        value.EmailFrom,
        value.EmailTo,
        value.DailyReportTimeKst,
        value.Tqqq200SmaAllowedSymbols,
        value.LastModified,
        OrderModeCatalog.All.Select(item => new SettingsOptionResponse(
            item.Code, item.DisplayName, item.Description)).ToArray(),
        DataProviderCatalog.Implemented.Select(item => new SettingsOptionResponse(
            item.Value.ToString(), item.DisplayName, $"{item.Market} 시장 데이터")).ToArray(),
        PatternCatalog.BuiltIn.Select(item => new SettingsOptionResponse(
            item.Code, item.DisplayName)).ToArray());
}

public sealed record SettingsUpdateRequest
{
    public required OrderMode OrderMode { get; init; }
    public required DataSource PreferredDataSource { get; init; }
    public required IReadOnlyList<PatternType> EnabledPatterns { get; init; }
    public required IReadOnlyList<string> WatchlistSymbols { get; init; }
    public required bool SoundAlerts { get; init; }
    public required decimal AccountSize { get; init; }
    public required decimal RiskPerTradePercent { get; init; }
    public required decimal DailyLossLimitPercent { get; init; }
    public required int MaxTotalPositions { get; init; }
    public required int MaxPositionsPerSector { get; init; }
    public required decimal MinExpectancy { get; init; }
    public string? LiveParameterOverridesJson { get; init; }
    public bool? EnableTelegram { get; init; }
    public string? TelegramBotToken { get; init; }
    public string? TelegramChatId { get; init; }
    public bool? EnableDiscord { get; init; }
    public string? DiscordWebhookUrl { get; init; }
    public bool? EnableEmail { get; init; }
    public string? SmtpHost { get; init; }
    public int? SmtpPort { get; init; }
    public bool? SmtpUseSsl { get; init; }
    public string? SmtpUsername { get; init; }
    public string? SmtpPassword { get; init; }
    public string? EmailFrom { get; init; }
    public string? EmailTo { get; init; }
    public string? DailyReportTimeKst { get; init; }
    public string? Tqqq200SmaAllowedSymbols { get; init; }

    public SettingsUpdateCommand ToCommand() => new()
    {
        OrderMode = OrderMode,
        PreferredDataSource = PreferredDataSource,
        EnabledPatterns = EnabledPatterns,
        WatchlistSymbols = WatchlistSymbols,
        SoundAlerts = SoundAlerts,
        AccountSize = AccountSize,
        RiskPerTradePercent = RiskPerTradePercent,
        DailyLossLimitPercent = DailyLossLimitPercent,
        MaxTotalPositions = MaxTotalPositions,
        MaxPositionsPerSector = MaxPositionsPerSector,
        MinExpectancy = MinExpectancy,
        LiveParameterOverridesJson = LiveParameterOverridesJson,
        EnableTelegram = EnableTelegram,
        TelegramBotToken = TelegramBotToken,
        TelegramChatId = TelegramChatId,
        EnableDiscord = EnableDiscord,
        DiscordWebhookUrl = DiscordWebhookUrl,
        EnableEmail = EnableEmail,
        SmtpHost = SmtpHost,
        SmtpPort = SmtpPort,
        SmtpUseSsl = SmtpUseSsl,
        SmtpUsername = SmtpUsername,
        SmtpPassword = SmtpPassword,
        EmailFrom = EmailFrom,
        EmailTo = EmailTo,
        DailyReportTimeKst = DailyReportTimeKst,
        Tqqq200SmaAllowedSymbols = Tqqq200SmaAllowedSymbols
    };
}

public sealed record SettingsUpdateResponse(string Message, DateTime LastModified);
public sealed record SettingsErrorResponse(IReadOnlyList<string> Errors);
