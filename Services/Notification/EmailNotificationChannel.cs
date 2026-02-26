using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// System.Net.Mail.SmtpClient를 사용하는 이메일 알림 채널.
/// 시그널 즉시 알림과 일일 요약 리포트 모두 지원.
/// </summary>
public sealed class EmailNotificationChannel : INotificationChannel
{
    private readonly NotificationSettings _settings;
    private readonly ILogger<EmailNotificationChannel> _logger;

    public string ChannelName => "Email";

    public bool IsEnabled =>
        _settings.EnableEmail &&
        !string.IsNullOrWhiteSpace(_settings.SmtpHost) &&
        !string.IsNullOrWhiteSpace(_settings.EmailFrom) &&
        !string.IsNullOrWhiteSpace(_settings.EmailTo);

    public EmailNotificationChannel(
        IOptions<NotificationSettings> settings,
        ILogger<EmailNotificationChannel> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        var isLong = recommendation.TargetPrice >= recommendation.EntryPrice;
        var direction = isLong ? "매수 (Long)" : "매도 (Short)";
        var stopPct = (recommendation.StopLossPercent * 100m).ToString("F2");
        var subject = $"[StockTrader] 새 시그널: {recommendation.Symbol} {direction}";

        var body = BuildSignalEmailHtml(recommendation, direction, stopPct);
        await SendEmailAsync(subject, body, isHtml: true, ct);
        _logger.LogInformation("Email signal sent for {Symbol}", recommendation.Symbol);
    }

    public async Task SendAlertAsync(string message, CancellationToken ct = default)
    {
        var subject = "[StockTrader] 리스크 경고";
        var body = $"""
            <html><body style="font-family:Arial,sans-serif;padding:20px;">
            <h2 style="color:#E67E22;">경고</h2>
            <p style="font-size:16px;">{System.Net.WebUtility.HtmlEncode(message)}</p>
            <hr/>
            <p style="color:#999;font-size:12px;">StockTrader &mdash; {DateTime.Now:yyyy-MM-dd HH:mm}</p>
            </body></html>
            """;
        await SendEmailAsync(subject, body, isHtml: true, ct);
    }

    public async Task SendDailyReportAsync(DailyReportData report, CancellationToken ct = default)
    {
        var subject = $"[StockTrader] 일일 리포트 {report.ReportDate:yyyy-MM-dd}";
        var body = BuildDailyReportHtml(report);
        await SendEmailAsync(subject, body, isHtml: true, ct);
        _logger.LogInformation("Email daily report sent for {Date}", report.ReportDate);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await SendEmailAsync(
                "[StockTrader] 이메일 연결 테스트",
                "<p>StockTrader 이메일 알림 연결 테스트 성공!</p>",
                isHtml: true,
                ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email connection test failed");
            return false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────

    private async Task SendEmailAsync(string subject, string body, bool isHtml, CancellationToken ct)
    {
        // SmtpClient는 IDisposable이므로 using 블록에서 관리
        using var client = CreateSmtpClient();
        using var message = new MailMessage(
            from: new MailAddress(_settings.EmailFrom),
            to: new MailAddress(_settings.EmailTo))
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = isHtml,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };

        // SmtpClient.SendMailAsync는 CancellationToken을 직접 받지 않으므로
        // TaskCompletionSource로 취소 지원을 래핑한다.
        await SendWithCancellationAsync(client, message, ct);
    }

    private SmtpClient CreateSmtpClient()
    {
        var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort)
        {
            EnableSsl = _settings.SmtpUseSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(_settings.SmtpUsername))
        {
            client.Credentials = new NetworkCredential(
                _settings.SmtpUsername,
                _settings.SmtpPassword);
        }

        return client;
    }

    private static async Task SendWithCancellationAsync(
        SmtpClient client, MailMessage message, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = ct.Register(() =>
            tcs.TrySetCanceled(ct));

        var sendTask = client.SendMailAsync(message);

        var completed = await Task.WhenAny(sendTask, tcs.Task);

        if (completed == tcs.Task)
        {
            // 취소 요청: SMTP 전송 중단 시도
            client.SendAsyncCancel();
            ct.ThrowIfCancellationRequested();
        }

        await sendTask; // 실제 예외 전파
    }

    // ── HTML builders ─────────────────────────────────────────────────

    private static string BuildSignalEmailHtml(
        TradeRecommendation rec, string direction, string stopPct)
    {
        var pnlColor = rec.TargetPrice >= rec.EntryPrice ? "#27AE60" : "#E74C3C";
        return $"""
            <html><body style="font-family:Arial,sans-serif;padding:20px;max-width:600px;">
            <h2 style="color:{pnlColor};">새 시그널: {rec.Symbol} &mdash; {direction}</h2>
            <table style="border-collapse:collapse;width:100%;">
              <tr style="background:#f2f2f2;">
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">패턴</td>
                <td style="padding:8px;border:1px solid #ddd;">{rec.PatternType}</td>
              </tr>
              <tr>
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">진입가</td>
                <td style="padding:8px;border:1px solid #ddd;">${rec.EntryPrice:F2}</td>
              </tr>
              <tr style="background:#f2f2f2;">
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">손절가</td>
                <td style="padding:8px;border:1px solid #ddd;color:#E74C3C;">${rec.StopLossPrice:F2} (-{stopPct}%)</td>
              </tr>
              <tr>
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">목표가</td>
                <td style="padding:8px;border:1px solid #ddd;color:#27AE60;">${rec.TargetPrice:F2}</td>
              </tr>
              <tr style="background:#f2f2f2;">
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">손익비</td>
                <td style="padding:8px;border:1px solid #ddd;">{rec.RiskRewardRatio:F2}R</td>
              </tr>
              <tr>
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">수량</td>
                <td style="padding:8px;border:1px solid #ddd;">{rec.ShareQuantity}주 (${rec.PositionSize:N0})</td>
              </tr>
              <tr style="background:#f2f2f2;">
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">기대값</td>
                <td style="padding:8px;border:1px solid #ddd;">{rec.Expectancy:F3}</td>
              </tr>
            </table>
            <hr style="margin-top:20px;"/>
            <p style="color:#999;font-size:12px;">StockTrader &mdash; {rec.GeneratedAt:yyyy-MM-dd HH:mm} ET</p>
            </body></html>
            """;
    }

    private static string BuildDailyReportHtml(DailyReportData report)
    {
        var pnlSign = report.DailyPnl >= 0 ? "+" : "";
        var pnlColor = report.DailyPnl >= 0 ? "#27AE60" : "#E74C3C";
        var topSignalsHtml = report.TopSignals.Count > 0
            ? string.Join("", report.TopSignals.Select(s =>
                $"<li style='margin:4px 0;'>{System.Net.WebUtility.HtmlEncode(s)}</li>"))
            : "<li>없음</li>";
        var executedHtml = report.ExecutedSymbols.Count > 0
            ? string.Join(", ", report.ExecutedSymbols.Select(s =>
                $"<strong>{System.Net.WebUtility.HtmlEncode(s)}</strong>"))
            : "없음";

        return $"""
            <html><body style="font-family:Arial,sans-serif;padding:20px;max-width:600px;">
            <h2 style="color:#2C3E50;">일일 트레이딩 리포트</h2>
            <p style="color:#7F8C8D;">{report.ReportDate:yyyy년 M월 d일}</p>

            <table style="border-collapse:collapse;width:100%;margin-bottom:20px;">
              <tr style="background:#f2f2f2;">
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">시그널 발생</td>
                <td style="padding:8px;border:1px solid #ddd;">{report.TotalSignals}건</td>
              </tr>
              <tr>
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">체결 거래</td>
                <td style="padding:8px;border:1px solid #ddd;">{report.ExecutedTrades}건</td>
              </tr>
              <tr style="background:#f2f2f2;">
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">일일 손익</td>
                <td style="padding:8px;border:1px solid #ddd;color:{pnlColor};font-weight:bold;">
                  {pnlSign}${report.DailyPnl:N2} ({pnlSign}{report.DailyPnlPercent:F2}%)
                </td>
              </tr>
              <tr>
                <td style="padding:8px;border:1px solid #ddd;font-weight:bold;">시장 레짐</td>
                <td style="padding:8px;border:1px solid #ddd;">{report.MarketRegimeSummary}</td>
              </tr>
            </table>

            <h3>체결 종목</h3>
            <p>{executedHtml}</p>

            <h3>주요 시그널</h3>
            <ul style="padding-left:20px;">{topSignalsHtml}</ul>

            <hr style="margin-top:20px;"/>
            <p style="color:#999;font-size:12px;">StockTrader Daily Report &mdash; 자동 생성된 리포트입니다.</p>
            </body></html>
            """;
    }
}
