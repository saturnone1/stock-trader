using Microsoft.Extensions.Options;
using StockTrader.Application.Reporting;
using StockTrader.Configuration;
using StockTrader.Models;

namespace StockTrader.Services.Notification;

/// <summary>
/// 모든 활성 INotificationChannel 구현체에 알림을 병렬로 발송한다.
/// 채널별 실패는 독립적으로 처리되어 다른 채널에 영향을 주지 않는다.
/// 재시도 로직: NotificationSettings.MaxRetryAttempts 설정에 따라 지수 백오프로 재시도.
/// 채널 활성 여부는 DB UserSettings를 우선 확인하고, 없으면 appsettings.json을 fallback으로 사용한다.
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher, IDailyReportPublisher
{
    private readonly IEnumerable<INotificationChannel> _channels;
    private readonly INotificationSettingsProvider _settingsProvider;
    private readonly NotificationSettings _appSettings;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        IEnumerable<INotificationChannel> channels,
        INotificationSettingsProvider settingsProvider,
        IOptions<NotificationSettings> appSettings,
        ILogger<NotificationDispatcher> logger)
    {
        _channels         = channels;
        _settingsProvider = settingsProvider;
        _appSettings      = appSettings.Value;
        _logger           = logger;
    }

    public async Task DispatchSignalAsync(TradeRecommendation recommendation, CancellationToken ct = default)
    {
        var activeChannels = await GetActiveChannelsAsync(ct);
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
        var activeChannels = await GetActiveChannelsAsync(ct);
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

    public async Task PublishAsync(DailyReportData report, CancellationToken ct = default)
    {
        var activeChannels = await GetActiveChannelsAsync(ct);
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
        NotificationSettings? effectiveSettings = null;

        try
        {
            effectiveSettings = await _settingsProvider.GetEffectiveSettingsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB 설정 조회 실패 — appsettings 기준으로 채널 테스트");
        }

        foreach (var channel in _channels)
        {
            var isActive = effectiveSettings != null
                ? IsChannelActive(channel, effectiveSettings)
                : channel.IsEnabled;

            if (!isActive)
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

        // DB 설정 우선으로 채널 활성 여부 확인
        bool isActive;
        try
        {
            var effectiveSettings = await _settingsProvider.GetEffectiveSettingsAsync(ct);
            isActive = IsChannelActive(channel, effectiveSettings);
        }
        catch
        {
            isActive = channel.IsEnabled; // fallback
        }

        if (!isActive)
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

    /// <summary>
    /// DB UserSettings와 appsettings를 병합한 유효 설정을 기반으로
    /// 활성화된 채널 목록을 반환한다.
    /// DB에 채널별 활성 여부가 설정되어 있으면 appsettings보다 우선 적용한다.
    /// </summary>
    private async Task<List<INotificationChannel>> GetActiveChannelsAsync(CancellationToken ct)
    {
        try
        {
            var effectiveSettings = await _settingsProvider.GetEffectiveSettingsAsync(ct);
            return _channels.Where(c => IsChannelActive(c, effectiveSettings)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DB 설정 조회 실패 — appsettings 기준으로 채널 활성 여부 판단");
            return _channels.Where(c => c.IsEnabled).ToList();
        }
    }

    /// <summary>
    /// 채널명을 기준으로 병합 설정에서 해당 채널의 활성 여부를 확인한다.
    /// </summary>
    private static bool IsChannelActive(INotificationChannel channel, NotificationSettings settings) =>
        channel.ChannelName switch
        {
            "Telegram" => settings.EnableTelegram
                          && !string.IsNullOrWhiteSpace(settings.TelegramBotToken)
                          && !string.IsNullOrWhiteSpace(settings.TelegramChatId),
            "Discord"  => settings.EnableDiscord
                          && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl),
            "Email"    => settings.EnableEmail
                          && !string.IsNullOrWhiteSpace(settings.SmtpHost)
                          && !string.IsNullOrWhiteSpace(settings.EmailFrom)
                          && !string.IsNullOrWhiteSpace(settings.EmailTo),
            _          => channel.IsEnabled  // 알 수 없는 채널은 기존 IsEnabled 그대로 사용
        };

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
        var maxAttempts = Math.Max(1, _appSettings.MaxRetryAttempts);
        var baseDelay = TimeSpan.FromSeconds(Math.Max(1, _appSettings.RetryDelaySeconds));

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
