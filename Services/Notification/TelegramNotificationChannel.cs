using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Notification;

/// <summary>
/// Telegram Bot API를 직접 호출하는 알림 채널.
/// 외부 NuGet 패키지 없이 HttpClient만 사용.
/// API 문서: https://core.telegram.org/bots/api#sendmessage
/// </summary>
public sealed class TelegramNotificationChannel : INotificationChannel
{
    private const string ApiBaseUrl = "https://api.telegram.org";

    private readonly HttpClient _http;
    private readonly NotificationSettings _settings;
    private readonly ILogger<TelegramNotificationChannel> _logger;

    public string ChannelName => "Telegram";

    public bool IsEnabled =>
        _settings.EnableTelegram &&
        !string.IsNullOrWhiteSpace(_settings.TelegramBotToken) &&
        !string.IsNullOrWhiteSpace(_settings.TelegramChatId);

    public TelegramNotificationChannel(
        HttpClient http,
        IOptions<NotificationSettings> settings,
        ILogger<TelegramNotificationChannel> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        var direction = GetDirectionLabel(recommendation);
        var rr = recommendation.RiskRewardRatio.ToString("F2");
        var stopPct = (recommendation.StopLossPercent * 100m).ToString("F2");

        var text = $"""
            <b>새 시그널: {recommendation.Symbol}</b> {direction}

            패턴: <code>{GetPatternKorean(recommendation.PatternType)}</code>
            진입가: <b>${recommendation.EntryPrice:F2}</b>
            손절가: <b>${recommendation.StopLossPrice:F2}</b> (-{stopPct}%)
            목표가: <b>${recommendation.TargetPrice:F2}</b>
            손익비: <b>{rr}R</b>
            수량: <b>{recommendation.ShareQuantity}주</b> (${recommendation.PositionSize:N0})
            기대값: <b>{recommendation.Expectancy:F3}</b>

            <i>{recommendation.GeneratedAt:yyyy-MM-dd HH:mm} ET</i>
            """;

        await SendMessageAsync(text, ct);
        _logger.LogInformation("Telegram signal sent for {Symbol}", recommendation.Symbol);
    }

    public async Task SendAlertAsync(string message, CancellationToken ct = default)
    {
        var text = $"<b>알림</b>\n\n{EscapeHtml(message)}";
        await SendMessageAsync(text, ct);
    }

    public async Task SendDailyReportAsync(DailyReportData report, CancellationToken ct = default)
    {
        var pnlSign = report.DailyPnl >= 0 ? "+" : "";
        var pnlEmoji = report.DailyPnl >= 0 ? "초록불" : "빨간불";

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

        await SendMessageAsync(text, ct);
        _logger.LogInformation("Telegram daily report sent for {Date}", report.ReportDate);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await SendMessageAsync("StockTrader 텔레그램 연결 테스트 성공!", ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram connection test failed");
            return false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────

    private async Task SendMessageAsync(string htmlText, CancellationToken ct)
    {
        var url = $"{ApiBaseUrl}/bot{_settings.TelegramBotToken}/sendMessage";

        var payload = new
        {
            chat_id = _settings.TelegramChatId,
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

    private static string GetPatternKorean(PatternType pattern) => pattern switch
    {
        PatternType.GapUpPullback => "갭 업 풀백",
        PatternType.Breakout => "돌파 매수",
        PatternType.VwapReversion => "VWAP 반등",
        PatternType.RsiMeanReversion => "RSI 평균 회귀",
        PatternType.TrendPullback => "트렌드 풀백",
        PatternType.OpeningRangeBreakout => "오프닝 레인지 돌파",
        PatternType.VolumeSpikeContinuation => "거래량 급증 지속",
        PatternType.EarningsDrift => "어닝 드리프트",
        PatternType.IndexRegimeFilter => "지수 레짐 필터",
        PatternType.VolatilityExpansion => "변동성 확장",
        PatternType.MomentumReversal => "모멘텀 반전",
        PatternType.MultiTimeframeTrend => "멀티 타임프레임 트렌드",
        PatternType.MeanReversionChannel => "평균 회귀 채널",
        _ => pattern.ToString()
    };

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
}
