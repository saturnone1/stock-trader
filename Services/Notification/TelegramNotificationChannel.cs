using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Application.Reporting;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Notification;

/// <summary>
/// Telegram Bot API를 직접 호출하는 알림 채널.
/// 외부 NuGet 패키지 없이 HttpClient만 사용.
/// API 문서: https://core.telegram.org/bots/api#sendmessage
/// 설정 우선순위: DB UserSettings > appsettings.json (INotificationSettingsProvider를 통해 병합).
/// </summary>
public sealed class TelegramNotificationChannel : INotificationChannel
{
    private const string ApiBaseUrl = "https://api.telegram.org";

    private readonly HttpClient _http;
    private readonly INotificationSettingsProvider _settingsProvider;
    private readonly NotificationSettings _fallbackSettings;
    private readonly ILogger<TelegramNotificationChannel> _logger;

    public string ChannelName => "Telegram";

    // IsEnabled는 동기 프로퍼티이므로 fallback(appsettings) 기준으로만 판별.
    // 실제 발송 시점에 GetEffectiveSettingsAsync()로 재확인한다.
    public bool IsEnabled =>
        _fallbackSettings.EnableTelegram &&
        !string.IsNullOrWhiteSpace(_fallbackSettings.TelegramBotToken) &&
        !string.IsNullOrWhiteSpace(_fallbackSettings.TelegramChatId);

    public TelegramNotificationChannel(
        HttpClient http,
        INotificationSettingsProvider settingsProvider,
        IOptions<NotificationSettings> fallbackSettings,
        ILogger<TelegramNotificationChannel> logger)
    {
        _http             = http;
        _settingsProvider = settingsProvider;
        _fallbackSettings = fallbackSettings.Value;
        _logger           = logger;
    }

    // 런타임에 항상 병합된 최신 설정을 조회한다 (DB 우선, fallback은 appsettings).
    private Task<NotificationSettings> GetSettingsAsync(CancellationToken ct) =>
        _settingsProvider.GetEffectiveSettingsAsync(ct);

    public async Task SendSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.EnableTelegram
            || string.IsNullOrWhiteSpace(settings.TelegramBotToken)
            || string.IsNullOrWhiteSpace(settings.TelegramChatId))
            return;

        var direction = GetDirectionLabel(recommendation);
        var rr = recommendation.RiskRewardRatio.ToString("F2");
        var stopPct = (recommendation.StopLossPercent * 100m).ToString("F2");

        var text = $"""
            <b>새 시그널: {recommendation.Symbol}</b> {direction}

            패턴: <code>{PatternCatalog.DisplayName(recommendation.PatternType, recommendation.CustomPatternName)}</code>
            진입가: <b>${recommendation.EntryPrice:F2}</b>
            손절가: <b>${recommendation.StopLossPrice:F2}</b> (-{stopPct}%)
            목표가: <b>${recommendation.TargetPrice:F2}</b>
            손익비: <b>{rr}R</b>
            수량: <b>{recommendation.ShareQuantity}주</b> (${recommendation.PositionSize:N0})
            기대값: <b>{recommendation.Expectancy:F3}</b>

            <i>{recommendation.GeneratedAt:yyyy-MM-dd HH:mm} ET</i>
            """;

        await SendMessageAsync(text, settings, ct);
        _logger.LogInformation("Telegram signal sent for {Symbol}", recommendation.Symbol);
    }

    public async Task SendAlertAsync(string message, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.EnableTelegram
            || string.IsNullOrWhiteSpace(settings.TelegramBotToken)
            || string.IsNullOrWhiteSpace(settings.TelegramChatId))
            return;

        var text = $"<b>알림</b>\n\n{EscapeHtml(message)}";
        await SendMessageAsync(text, settings, ct);
    }

    public async Task SendDailyReportAsync(DailyReportData report, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.EnableTelegram
            || string.IsNullOrWhiteSpace(settings.TelegramBotToken)
            || string.IsNullOrWhiteSpace(settings.TelegramChatId))
            return;

        var pnlSign = report.DailyPnl >= 0 ? "+" : "";

        var topSignalsText = report.TopSignals.Count > 0
            ? string.Join("\n", report.TopSignals.Select(s => $"  • {s}"))
            : "  없음";
        var executedText = report.ExecutedSymbols.Count > 0
            ? string.Join(", ", report.ExecutedSymbols)
            : "없음";

        var text = $"""
            <b>일일 트레이딩 리포트 ({report.ReportDate:yyyy-MM-dd})</b>

            시그널 발생: <b>{report.TotalSignals}건</b>
            체결 거래: <b>{report.ExecutedTrades}건</b>
            일일 손익: <b>{pnlSign}${report.DailyPnl:N2} ({pnlSign}{report.DailyPnlPercent:F2}%)</b>
            시장 레짐: <code>{report.MarketRegimeSummary}</code>

            주요 시그널:
            {topSignalsText}

            체결 종목: {executedText}
            """;

        await SendMessageAsync(text, settings, ct);
        _logger.LogInformation("Telegram daily report sent for {Date}", report.ReportDate);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await GetSettingsAsync(ct);
            await SendMessageAsync("StockTrader 텔레그램 연결 테스트 성공!", settings, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram connection test failed");
            return false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────

    private async Task SendMessageAsync(string htmlText, NotificationSettings settings, CancellationToken ct)
    {
        var url = $"{ApiBaseUrl}/bot{settings.TelegramBotToken}/sendMessage";

        var payload = new
        {
            chat_id = settings.TelegramChatId,
            text = htmlText,
            parse_mode = "HTML",
            disable_web_page_preview = true
        };

        var response = await _http.PostAsJsonAsync(url, payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Telegram API error {(int)response.StatusCode}: {body}");
        }
    }

    private static string GetDirectionLabel(TradeRecommendation rec)
    {
        // 목표가가 진입가보다 높으면 롱, 낮으면 숏
        return rec.TargetPrice >= rec.EntryPrice ? "[매수 롱]" : "[매도 숏]";
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}
