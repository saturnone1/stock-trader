using StockTrader.Domain.MarketData;

namespace StockTrader.Application.Settings;

public sealed class SettingsManagementService(
    ISettingsManagementStore store,
    TimeProvider timeProvider)
{
    private const int MaximumWatchlistSize = 100;

    public Task<ManagedSettings> GetAsync(CancellationToken ct = default) =>
        store.GetAsync(ct);

    public async Task<SettingsUpdateOutcome> UpdateAsync(
        SettingsUpdateCommand command,
        CancellationToken ct = default)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
            return new(null, errors);

        var current = await store.GetAsync(ct);
        var updated = current with
        {
            OrderMode = command.OrderMode,
            PreferredDataSource = command.PreferredDataSource,
            EnabledPatterns = (command.EnabledPatterns ?? []).Distinct().ToArray(),
            WatchlistSymbols = NormalizeSymbols(command.WatchlistSymbols ?? []),
            SoundAlerts = command.SoundAlerts,
            AccountSize = command.AccountSize,
            RiskPerTradePercent = command.RiskPerTradePercent,
            DailyLossLimitPercent = command.DailyLossLimitPercent,
            MaxTotalPositions = command.MaxTotalPositions,
            MaxPositionsPerSector = command.MaxPositionsPerSector,
            MinExpectancy = command.MinExpectancy,
            LiveParameterOverridesJson = command.LiveParameterOverridesJson
                ?? current.LiveParameterOverridesJson,
            EnableTelegram = command.EnableTelegram ?? current.EnableTelegram,
            TelegramBotToken = MergeSecret(current.TelegramBotToken, command.TelegramBotToken),
            TelegramChatId = command.TelegramChatId ?? current.TelegramChatId,
            EnableDiscord = command.EnableDiscord ?? current.EnableDiscord,
            DiscordWebhookUrl = MergeSecret(current.DiscordWebhookUrl, command.DiscordWebhookUrl),
            EnableEmail = command.EnableEmail ?? current.EnableEmail,
            SmtpHost = command.SmtpHost ?? current.SmtpHost,
            SmtpPort = command.SmtpPort ?? current.SmtpPort,
            SmtpUseSsl = command.SmtpUseSsl ?? current.SmtpUseSsl,
            SmtpUsername = command.SmtpUsername ?? current.SmtpUsername,
            SmtpPassword = MergeSecret(current.SmtpPassword, command.SmtpPassword),
            EmailFrom = command.EmailFrom ?? current.EmailFrom,
            EmailTo = command.EmailTo ?? current.EmailTo,
            DailyReportTimeKst = command.DailyReportTimeKst is null
                ? current.DailyReportTimeKst
                : NullIfWhiteSpace(command.DailyReportTimeKst),
            Tqqq200SmaAllowedSymbols = command.Tqqq200SmaAllowedSymbols is null
                ? current.Tqqq200SmaAllowedSymbols
                : string.Join(',', NormalizeSymbols([command.Tqqq200SmaAllowedSymbols])),
            LastModified = timeProvider.GetUtcNow().UtcDateTime
        };

        await store.SaveAsync(updated, ct);
        return new(updated, []);
    }

    private static List<string> Validate(SettingsUpdateCommand command)
    {
        var errors = new List<string>();
        if (!OrderModeCatalog.Contains(command.OrderMode))
            errors.Add("지원하지 않는 주문 방식입니다.");
        if (!DataProviderCatalog.All.Any(item => item.Value == command.PreferredDataSource && item.IsImplemented))
            errors.Add("현재 연결할 수 없는 시세 공급자입니다.");

        var builtIn = PatternCatalog.BuiltIn.Select(item => item.Value).ToHashSet();
        if (command.EnabledPatterns is null)
            errors.Add("실시간 감시 전략 목록이 필요합니다.");
        else if (command.EnabledPatterns.Any(item => !builtIn.Contains(item)))
            errors.Add("실시간 감시 목록에는 지원되는 내장 전략만 선택할 수 있습니다.");

        if (command.WatchlistSymbols is null)
        {
            errors.Add("관심종목 목록이 필요합니다.");
        }
        else
        {
            var symbols = NormalizeSymbols(command.WatchlistSymbols);
            if (symbols.Count > MaximumWatchlistSize)
                errors.Add($"관심종목은 최대 {MaximumWatchlistSize}개까지 저장할 수 있습니다.");
            if (symbols.Any(symbol => !MarketSymbolPolicy.IsValid(symbol)))
                errors.Add("관심종목에는 영문자, 숫자, 점(.)과 하이픈(-)만 사용할 수 있습니다.");
        }

        if (command.AccountSize <= 0)
            errors.Add("계좌 기준 금액은 0보다 커야 합니다.");
        if (command.RiskPerTradePercent is <= 0 or > 1)
            errors.Add("거래당 손실 허용률은 0보다 크고 1 이하여야 합니다.");
        if (command.DailyLossLimitPercent is <= 0 or > 1)
            errors.Add("일일 손실 한도는 0보다 크고 1 이하여야 합니다.");
        if (command.MaxTotalPositions <= 0)
            errors.Add("전체 최대 보유 종목 수는 1개 이상이어야 합니다.");
        if (command.MaxPositionsPerSector <= 0 || command.MaxPositionsPerSector > command.MaxTotalPositions)
            errors.Add("업종별 최대 보유 수는 1 이상이며 전체 최대 보유 수 이하여야 합니다.");
        if (command.MinExpectancy < 0)
            errors.Add("최소 기대값은 0 이상이어야 합니다.");
        if (command.SmtpPort is <= 0 or > 65535)
            errors.Add("SMTP 포트는 1~65535 범위여야 합니다.");
        if (!string.IsNullOrWhiteSpace(command.DailyReportTimeKst)
            && !TimeOnly.TryParseExact(command.DailyReportTimeKst, "HH:mm", out _))
            errors.Add("일일 리포트 시간은 HH:mm 형식이어야 합니다.");

        return errors;
    }

    private static string? MergeSecret(string? current, string? requested) =>
        requested is null ? current : NullIfWhiteSpace(requested);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeSymbols(IEnumerable<string> values) =>
        MarketSymbolPolicy.NormalizeMany(values);
}
