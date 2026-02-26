using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// 모든 활성 INotificationChannel 구현체에 알림을 병렬로 발송한다.
/// 채널별 실패는 독립적으로 처리되어 다른 채널에 영향을 주지 않는다.
/// 재시도 로직: NotificationSettings.MaxRetryAttempts 설정에 따라 지수 백오프로 재시도.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly NotificationSettings _settings;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        IOptions<NotificationSettings> settings,
        ILogger<NotificationDispatcher> logger)
    {
        _channels = channels;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task DispatchSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        var activeChannels = GetActiveChannels();
        if (activeChannels.Count == 0) return;

        _logger.LogInformation(
            "Dispatching signal notification for {Symbol} to {Count} channel(s)",
            recommendation.Symbol, activeChannels.Count);

        var tasks = activeChannels.Select(channel =>
            ExecuteWithRetryAsync(
                channel,
                c => c.SendSignalAsync(recommendation, ct),
                "SendSignal",
                ct));

        await Task.WhenAll(tasks);
    }

    public async Task DispatchAlertAsync(string message, CancellationToken ct = default)
    {
        var activeChannels = GetActiveChannels();
        if (activeChannels.Count == 0) return;

        _logger.LogInformation(
            "Dispatching alert to {Count} channel(s): {Message}",
            activeChannels.Count, message);

        var tasks = activeChannels.Select(channel =>
            ExecuteWithRetryAsync(
                channel,
                c => c.SendAlertAsync(message, ct),
                "SendAlert",
                ct));

        await Task.WhenAll(tasks);
    }

    public async Task DispatchDailyReportAsync(DailyReportData report, CancellationToken ct = default)
    {
        var activeChannels = GetActiveChannels();
        if (activeChannels.Count == 0)
        {
            _logger.LogDebug("No active notification channels — skipping daily report dispatch");
            return;
        }

        _logger.LogInformation(
            "Dispatching daily report for {Date} to {Count} channel(s)",
            report.ReportDate, activeChannels.Count);

        var tasks = activeChannels.Select(channel =>
            ExecuteWithRetryAsync(
                channel,
                c => c.SendDailyReportAsync(report, ct),
                "SendDailyReport",
                ct));

        await Task.WhenAll(tasks);
    }

    public async Task<Dictionary<string, bool>> TestAllChannelsAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, bool>();

        foreach (var channel in _channels)
        {
            if (!channel.IsEnabled)
            {
                results[channel.ChannelName] = false;
                continue;
            }

            try
            {
                results[channel.ChannelName] = await channel.TestConnectionAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Test failed for channel {Channel}", channel.ChannelName);
                results[channel.ChannelName] = false;
            }
        }

        return results;
    }

    public async Task<bool> TestChannelAsync(string channelName, CancellationToken ct = default)
    {
        var channel = _channels.FirstOrDefault(c =>
            string.Equals(c.ChannelName, channelName, StringComparison.OrdinalIgnoreCase));

        if (channel == null)
        {
            _logger.LogWarning("Channel '{ChannelName}' not found", channelName);
            return false;
        }

        if (!channel.IsEnabled)
        {
            _logger.LogWarning("Channel '{ChannelName}' is disabled — cannot test", channelName);
            return false;
        }

        try
        {
            return await channel.TestConnectionAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test failed for channel {Channel}", channelName);
            return false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────

    private List<INotificationChannel> GetActiveChannels() =>
        _channels.Where(c => c.IsEnabled).ToList();

    /// <summary>
    /// 지수 백오프(exponential backoff)로 최대 MaxRetryAttempts회 재시도.
    /// 채널 예외는 삼키고 경고 로그만 기록 (다른 채널에 영향 없도록).
    /// </summary>
    private async Task ExecuteWithRetryAsync(
        INotificationChannel channel,
        Func<INotificationChannel, Task> action,
        string operationName,
        CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, _settings.MaxRetryAttempts);
        var baseDelay = TimeSpan.FromSeconds(Math.Max(1, _settings.RetryDelaySeconds));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action(channel);
                return; // 성공
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 앱 종료 중 — 조용히 중단
                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    _logger.LogError(ex,
                        "Channel '{Channel}' {Operation} failed after {Attempts} attempts — giving up",
                        channel.ChannelName, operationName, maxAttempts);
                    return; // 모든 재시도 소진 → 다른 채널에 영향 없음
                }

                var delay = baseDelay * Math.Pow(2, attempt - 1); // 지수 백오프
                _logger.LogWarning(ex,
                    "Channel '{Channel}' {Operation} attempt {Attempt}/{Max} failed. Retrying in {Delay:F1}s",
                    channel.ChannelName, operationName, attempt, maxAttempts, delay.TotalSeconds);

                await Task.Delay(delay, ct);
            }
        }
    }
}
