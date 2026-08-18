namespace StockTrader.Application.Settings;

/// <summary>설정 관리 유스케이스가 사용하는 저장소 독립 스냅샷입니다.</summary>
public sealed record ManagedSettings
{
    public long Id { get; init; }
    public OrderMode OrderMode { get; init; } = OrderMode.AlertOnly;
    public DataSource PreferredDataSource { get; init; } = DataSource.Alpaca;
    public IReadOnlyList<PatternType> EnabledPatterns { get; init; } = [];
    public IReadOnlyList<string> WatchlistSymbols { get; init; } = [];
    public bool SoundAlerts { get; init; } = true;
    public decimal AccountSize { get; init; }
    public decimal RiskPerTradePercent { get; init; }
    public decimal DailyLossLimitPercent { get; init; }
    public int MaxTotalPositions { get; init; }
    public int MaxPositionsPerSector { get; init; }
    public decimal MinExpectancy { get; init; }
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
    public DateTime LastModified { get; init; }
}

public sealed record SettingsUpdateCommand
{
    public OrderMode OrderMode { get; init; }
    public DataSource PreferredDataSource { get; init; }
    public IReadOnlyList<PatternType> EnabledPatterns { get; init; } = [];
    public IReadOnlyList<string> WatchlistSymbols { get; init; } = [];
    public bool SoundAlerts { get; init; }
    public decimal AccountSize { get; init; }
    public decimal RiskPerTradePercent { get; init; }
    public decimal DailyLossLimitPercent { get; init; }
    public int MaxTotalPositions { get; init; }
    public int MaxPositionsPerSector { get; init; }
    public decimal MinExpectancy { get; init; }
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
}

public sealed record SettingsUpdateOutcome(
    ManagedSettings? Settings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Settings is not null && Errors.Count == 0;
}
