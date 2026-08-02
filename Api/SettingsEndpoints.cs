using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Api;

public static class SettingsEndpoints
{
    public static RouteGroupBuilder MapSettingsApi(this RouteGroupBuilder group)
    {
        // GET /api/settings
        group.MapGet("/settings", async (
            ISettingsRepository settingsRepo,
            CancellationToken ct) =>
        {
            var settings = await settingsRepo.GetAsync(ct);

            return Results.Ok(new
            {
                settings.Id,
                OrderMode             = settings.OrderMode.ToString(),
                PreferredDataSource   = settings.PreferredDataSource.ToString(),
                EnabledPatterns       = settings.EnabledPatterns.Select(p => p.ToString()),
                settings.WatchlistSymbols,
                settings.SoundAlerts,
                settings.AccountSize,
                settings.RiskPerTradePercent,
                settings.DailyLossLimitPercent,
                settings.MaxTotalPositions,
                settings.MaxPositionsPerSector,
                settings.MinExpectancy,
                settings.LiveParameterOverridesJson,
                // 알림 채널 설정 (민감 정보 마스킹)
                settings.EnableTelegram,
                TelegramBotToken  = MaskSecret(settings.TelegramBotToken),
                settings.TelegramChatId,
                settings.EnableDiscord,
                DiscordWebhookUrl = MaskSecret(settings.DiscordWebhookUrl),
                settings.EnableEmail,
                settings.SmtpHost,
                settings.SmtpPort,
                settings.SmtpUseSsl,
                settings.SmtpUsername,
                SmtpPassword      = MaskSecret(settings.SmtpPassword),
                settings.EmailFrom,
                settings.EmailTo,
                settings.DailyReportTimeKst,
                settings.Tqqq200SmaAllowedSymbols,
                LastModified      = settings.LastModified.ToString("o")
            });
        }).RequireAuthorization();

        // PUT /api/settings
        group.MapPut("/settings", async (
            HttpContext ctx,
            ISettingsRepository settingsRepo,
            CancellationToken ct) =>
        {
            UserSettings updateRequest;
            try
            {
                updateRequest = await ctx.Request.ReadFromJsonAsync<UserSettings>(ct)
                    ?? throw new InvalidOperationException("Request body is null.");
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid JSON body for UserSettings." });
            }

            // 현재 설정을 먼저 읽어 병합 (누락된 필드는 기존 값 유지)
            var current = await settingsRepo.GetAsync(ct);

            // OrderMode 유효성 검사
            if (!Enum.IsDefined(typeof(OrderMode), updateRequest.OrderMode))
                return Results.BadRequest(new { error = "Invalid OrderMode value." });

            // 설정 병합 적용
            current.OrderMode                = updateRequest.OrderMode;
            current.PreferredDataSource      = updateRequest.PreferredDataSource;
            current.EnabledPatterns          = updateRequest.EnabledPatterns ?? current.EnabledPatterns;
            current.WatchlistSymbols         = updateRequest.WatchlistSymbols ?? current.WatchlistSymbols;
            current.SoundAlerts              = updateRequest.SoundAlerts;
            current.AccountSize              = updateRequest.AccountSize > 0 ? updateRequest.AccountSize : current.AccountSize;
            current.RiskPerTradePercent      = updateRequest.RiskPerTradePercent > 0 ? updateRequest.RiskPerTradePercent : current.RiskPerTradePercent;
            current.DailyLossLimitPercent    = updateRequest.DailyLossLimitPercent > 0 ? updateRequest.DailyLossLimitPercent : current.DailyLossLimitPercent;
            current.MaxTotalPositions        = updateRequest.MaxTotalPositions > 0 ? updateRequest.MaxTotalPositions : current.MaxTotalPositions;
            current.MaxPositionsPerSector    = updateRequest.MaxPositionsPerSector > 0 ? updateRequest.MaxPositionsPerSector : current.MaxPositionsPerSector;
            current.MinExpectancy            = updateRequest.MinExpectancy;
            current.LiveParameterOverridesJson = updateRequest.LiveParameterOverridesJson ?? current.LiveParameterOverridesJson;
            // 알림 설정 (null이면 기존 값 유지)
            current.EnableTelegram           = updateRequest.EnableTelegram ?? current.EnableTelegram;
            current.TelegramBotToken         = updateRequest.TelegramBotToken ?? current.TelegramBotToken;
            current.TelegramChatId           = updateRequest.TelegramChatId ?? current.TelegramChatId;
            current.EnableDiscord            = updateRequest.EnableDiscord ?? current.EnableDiscord;
            current.DiscordWebhookUrl        = updateRequest.DiscordWebhookUrl ?? current.DiscordWebhookUrl;
            current.EnableEmail              = updateRequest.EnableEmail ?? current.EnableEmail;
            current.SmtpHost                 = updateRequest.SmtpHost ?? current.SmtpHost;
            current.SmtpPort                 = updateRequest.SmtpPort ?? current.SmtpPort;
            current.SmtpUseSsl               = updateRequest.SmtpUseSsl ?? current.SmtpUseSsl;
            current.SmtpUsername             = updateRequest.SmtpUsername ?? current.SmtpUsername;
            current.SmtpPassword             = updateRequest.SmtpPassword ?? current.SmtpPassword;
            current.EmailFrom                = updateRequest.EmailFrom ?? current.EmailFrom;
            current.EmailTo                  = updateRequest.EmailTo ?? current.EmailTo;
            current.DailyReportTimeKst       = updateRequest.DailyReportTimeKst ?? current.DailyReportTimeKst;
            current.Tqqq200SmaAllowedSymbols = updateRequest.Tqqq200SmaAllowedSymbols ?? current.Tqqq200SmaAllowedSymbols;
            current.LastModified             = DateTime.UtcNow;

            await settingsRepo.SaveAsync(current, ct);

            return Results.Ok(new { message = "설정이 저장되었습니다.", LastModified = current.LastModified.ToString("o") });
        }).RequireAuthorization();

        return group;
    }

    private static string? MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length <= 4) return "****";
        return value[..4] + new string('*', Math.Min(value.Length - 4, 8));
    }
}
