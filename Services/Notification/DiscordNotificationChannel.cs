using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// Discord Webhook을 통해 Embed 메시지를 발송하는 알림 채널.
/// 외부 NuGet 패키지 없이 HttpClient만 사용.
/// API 문서: https://discord.com/developers/docs/resources/webhook#execute-webhook
/// 설정 우선순위: DB UserSettings > appsettings.json (INotificationSettingsProvider를 통해 병합).
/// </summary>
public sealed class DiscordNotificationChannel : INotificationChannel
{
    // Discord embed 색상 (decimal)
    private const int ColorGreen = 3066993;   // #2ECC71 매수
    private const int ColorRed   = 15158332;  // #E74C3C 매도
    private const int ColorBlue  = 3447003;   // #3498DB 정보
    private const int ColorOrange = 15105570; // #E67E22 경고

    private readonly HttpClient _http;
    private readonly INotificationSettingsProvider _settingsProvider;
    private readonly NotificationSettings _fallbackSettings;
    private readonly ILogger<DiscordNotificationChannel> _logger;

    public string ChannelName => "Discord";

    // IsEnabled는 동기 프로퍼티이므로 fallback(appsettings) 기준으로만 판별.
    // 실제 발송 시점에 GetEffectiveSettingsAsync()로 재확인한다.
    public bool IsEnabled =>
        _fallbackSettings.EnableDiscord &&
        !string.IsNullOrWhiteSpace(_fallbackSettings.DiscordWebhookUrl);

    public DiscordNotificationChannel(
        HttpClient http,
        INotificationSettingsProvider settingsProvider,
        IOptions<NotificationSettings> fallbackSettings,
        ILogger<DiscordNotificationChannel> logger)
    {
        _http             = http;
        _settingsProvider = settingsProvider;
        _fallbackSettings = fallbackSettings.Value;
        _logger           = logger;
    }

    private Task<NotificationSettings> GetSettingsAsync(CancellationToken ct) =>
        _settingsProvider.GetEffectiveSettingsAsync(ct);

    public async Task SendSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.EnableDiscord || string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
            return;

        var isLong = recommendation.TargetPrice >= recommendation.EntryPrice;
        var color = isLong ? ColorGreen : ColorRed;
        var directionLabel = isLong ? "매수 (Long)" : "매도 (Short)";
        var stopPct = (recommendation.StopLossPercent * 100m).ToString("F2");

        var payload = new
        {
            username = "StockTrader Bot",
            embeds = new[]
            {
                new
                {
                    title = $"새 시그널: {recommendation.Symbol}  {directionLabel}",
                    color,
                    fields = new object[]
                    {
                        new { name = "패턴",   value = recommendation.PatternType.ToString(), inline = true },
                        new { name = "진입가", value = $"${recommendation.EntryPrice:F2}",    inline = true },
                        new { name = "\u200b", value = "\u200b",                              inline = true }, // spacer
                        new { name = "손절가", value = $"${recommendation.StopLossPrice:F2} (-{stopPct}%)", inline = true },
                        new { name = "목표가", value = $"${recommendation.TargetPrice:F2}",  inline = true },
                        new { name = "손익비", value = $"{recommendation.RiskRewardRatio:F2}R", inline = true },
                        new { name = "수량",   value = $"{recommendation.ShareQuantity}주 (${recommendation.PositionSize:N0})", inline = true },
                        new { name = "기대값", value = recommendation.Expectancy.ToString("F3"), inline = true }
                    },
                    footer = new { text = $"StockTrader  •  {recommendation.GeneratedAt:yyyy-MM-dd HH:mm} ET" },
                    timestamp = recommendation.GeneratedAt.ToString("o")
                }
            }
        };

        await PostWebhookAsync(payload, settings.DiscordWebhookUrl, ct);
        _logger.LogInformation("Discord signal sent for {Symbol}", recommendation.Symbol);
    }

    public async Task SendAlertAsync(string message, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.EnableDiscord || string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
            return;

        var payload = new
        {
            username = "StockTrader Bot",
            embeds = new[]
            {
                new
                {
                    title = "알림",
                    description = message,
                    color = ColorOrange,
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            }
        };

        await PostWebhookAsync(payload, settings.DiscordWebhookUrl, ct);
    }

    public async Task SendDailyReportAsync(DailyReportData report, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (!settings.EnableDiscord || string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
            return;

        var pnlSign = report.DailyPnl >= 0 ? "+" : "";
        var color = report.DailyPnl >= 0 ? ColorGreen : ColorRed;
        var topSignalsText = report.TopSignals.Count > 0
            ? string.Join("\n", report.TopSignals.Select(s => $"• {s}"))
            : "없음";
        var executedText = report.ExecutedSymbols.Count > 0
            ? string.Join(", ", report.ExecutedSymbols)
            : "없음";

        var payload = new
        {
            username = "StockTrader Bot",
            embeds = new[]
            {
                new
                {
                    title = $"일일 트레이딩 리포트  {report.ReportDate:yyyy-MM-dd}",
                    color,
                    fields = new object[]
                    {
                        new { name = "시그널 발생", value = $"{report.TotalSignals}건",    inline = true },
                        new { name = "체결 거래",   value = $"{report.ExecutedTrades}건", inline = true },
                        new { name = "일일 손익",   value = $"{pnlSign}${report.DailyPnl:N2} ({pnlSign}{report.DailyPnlPercent:F2}%)", inline = true },
                        new { name = "시장 레짐",   value = report.MarketRegimeSummary,   inline = true },
                        new { name = "체결 종목",   value = executedText,                  inline = false },
                        new { name = "주요 시그널", value = topSignalsText,                inline = false }
                    },
                    footer = new { text = "StockTrader Daily Report" },
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            }
        };

        await PostWebhookAsync(payload, settings.DiscordWebhookUrl, ct);
        _logger.LogInformation("Discord daily report sent for {Date}", report.ReportDate);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var settings = await GetSettingsAsync(ct);
            var payload = new
            {
                username = "StockTrader Bot",
                content = "StockTrader Discord 연결 테스트 성공!"
            };
            await PostWebhookAsync(payload, settings.DiscordWebhookUrl, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Discord connection test failed");
            return false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────

    private async Task PostWebhookAsync(object payload, string webhookUrl, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(webhookUrl, payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Discord webhook error {(int)response.StatusCode}: {body}");
        }
    }
}
