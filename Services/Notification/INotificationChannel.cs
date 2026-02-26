using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// 외부 알림 채널(Telegram, Discord, Email 등)의 공통 계약.
/// NotificationDispatcher가 이 인터페이스를 통해 채널을 다형적으로 호출한다.
/// </summary>
public interface INotificationChannel
{
    /// <summary>채널 이름 (로깅 및 UI 표시용)</summary>
    string ChannelName { get; }

    /// <summary>현재 설정 기준으로 채널이 활성화되어 있는지 여부</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 새 매매 시그널 알림 발송.
    /// 구현체는 반드시 예외를 외부로 전파해야 한다 (재시도 로직은 Dispatcher에서 처리).
    /// </summary>
    Task SendSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default);

    /// <summary>리스크 경고 등 일반 텍스트 알림 발송</summary>
    Task SendAlertAsync(string message, CancellationToken ct = default);

    /// <summary>일일 요약 리포트 발송</summary>
    Task SendDailyReportAsync(DailyReportData report, CancellationToken ct = default);

    /// <summary>채널 연결 테스트 (Settings 페이지 "테스트 발송" 버튼용)</summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>일일 요약 리포트에 담을 데이터</summary>
public record DailyReportData(
    DateOnly ReportDate,
    int TotalSignals,
    int ExecutedTrades,
    decimal DailyPnl,
    decimal DailyPnlPercent,
    IReadOnlyList<string> TopSignals,
    IReadOnlyList<string> ExecutedSymbols,
    string MarketRegimeSummary
);
