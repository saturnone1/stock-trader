using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StockTrader.Configuration;
using StockTrader.Models;
using StockTrader.Services.Notification;

namespace StockTrader.Tests;

/// <summary>
/// NotificationDispatcher 단위 테스트.
///
/// 핵심 불변식:
///  - 활성 채널(IsEnabled=true)에만 발송한다.
///  - 채널별 실패는 독립적으로 처리된다 (다른 채널에 영향 없음).
///  - MaxRetryAttempts 횟수만큼 재시도 후 포기한다.
///  - TestAllChannelsAsync: 비활성 채널은 false 반환, 연결 실패는 false 반환.
///  - TestChannelAsync: 채널 미존재·비활성 → false, 활성 → TestConnectionAsync 결과.
///
/// 주의: ExecuteWithRetryAsync 내부의 Task.Delay는 Math.Max(1, RetryDelaySeconds)초로
///       최소 1초가 걸린다. 재시도 횟수 검증 테스트는 MaxRetryAttempts=1로 설정하여
///       재시도 딜레이 없이 즉시 실패-포기 경로를 검증한다.
/// </summary>
public class NotificationDispatcherTests
{
    // ── 테스트 픽스처 헬퍼 ──────────────────────────────────────────────

    private static NotificationSettings DefaultSettings(
        int maxRetryAttempts = 1,
        int retryDelaySeconds = 0) =>
        new()
        {
            MaxRetryAttempts = maxRetryAttempts,
            RetryDelaySeconds = retryDelaySeconds  // Math.Max(1,0)=1이므로 재시도 시 1s 대기
        };

    private static NotificationDispatcher CreateSut(
        IEnumerable<INotificationChannel> channels,
        NotificationSettings? settings = null)
    {
        var opts = Options.Create(settings ?? DefaultSettings());
        return new NotificationDispatcher(
            channels,
            opts,
            NullLogger<NotificationDispatcher>.Instance);
    }

    private static TradeRecommendation CreateRecommendation(string symbol = "AAPL") =>
        new()
        {
            Symbol = symbol,
            EntryPrice = 150m,
            StopLossPrice = 145m,
            TargetPrice = 160m
        };

    private static DailyReportData CreateDailyReport() =>
        new(
            ReportDate: DateOnly.FromDateTime(DateTime.Today),
            TotalSignals: 5,
            ExecutedTrades: 2,
            DailyPnl: 1250m,
            DailyPnlPercent: 0.0125m,
            TopSignals: new[] { "AAPL", "TSLA" },
            ExecutedSymbols: new[] { "AAPL", "MSFT" },
            MarketRegimeSummary: "Trending Bullish"
        );

    /// <summary>
    /// IsEnabled 프로퍼티를 동적으로 설정 가능한 채널 Mock을 생성한다.
    /// </summary>
    private static Mock<INotificationChannel> CreateChannelMock(
        string name = "TestChannel",
        bool isEnabled = true)
    {
        var mock = new Mock<INotificationChannel>();
        mock.Setup(c => c.ChannelName).Returns(name);
        mock.Setup(c => c.IsEnabled).Returns(isEnabled);
        return mock;
    }

    // ══════════════════════════════════════════════════════════════════
    // 1. DispatchSignalAsync 테스트
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchSignal_NoActiveChannels_DoesNothing()
    {
        // Arrange: 비활성 채널만 존재
        var disabledChannel = CreateChannelMock("Telegram", isEnabled: false);
        var sut = CreateSut(new[] { disabledChannel.Object });
        var rec = CreateRecommendation();

        // Act
        await sut.DispatchSignalAsync(rec);

        // Assert: 비활성 채널이므로 SendSignalAsync 호출 없음
        disabledChannel.Verify(
            c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DispatchSignal_ActiveChannel_CallsSendSignal()
    {
        // Arrange
        var channel = CreateChannelMock("Telegram", isEnabled: true);
        channel
            .Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(new[] { channel.Object });
        var rec = CreateRecommendation("MSFT");

        // Act
        await sut.DispatchSignalAsync(rec);

        // Assert: 정확히 1회 호출, 동일한 recommendation 전달
        channel.Verify(
            c => c.SendSignalAsync(
                It.Is<TradeRecommendation>(r => r.Symbol == "MSFT"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchSignal_MultipleChannels_CallsAll()
    {
        // Arrange: 활성 채널 3개
        var telegram = CreateChannelMock("Telegram", isEnabled: true);
        var discord = CreateChannelMock("Discord", isEnabled: true);
        var email = CreateChannelMock("Email", isEnabled: true);

        foreach (var ch in new[] { telegram, discord, email })
        {
            ch.Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        }

        var sut = CreateSut(new[] { telegram.Object, discord.Object, email.Object });
        var rec = CreateRecommendation();

        // Act
        await sut.DispatchSignalAsync(rec);

        // Assert: 세 채널 모두 정확히 1회씩 호출
        foreach (var ch in new[] { telegram, discord, email })
        {
            ch.Verify(
                c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task DispatchSignal_ChannelFails_DoesNotAffectOthers()
    {
        // Arrange: 첫 번째 채널은 예외 발생, 두 번째는 정상
        // MaxRetryAttempts=1 → 재시도 없이 즉시 포기 (Task.Delay 없음)
        var failingChannel = CreateChannelMock("Discord", isEnabled: true);
        failingChannel
            .Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Discord webhook error"));

        var successChannel = CreateChannelMock("Telegram", isEnabled: true);
        successChannel
            .Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(
            new[] { failingChannel.Object, successChannel.Object },
            DefaultSettings(maxRetryAttempts: 1));

        var rec = CreateRecommendation();

        // Act: 예외가 전파되지 않아야 함
        var act = async () => await sut.DispatchSignalAsync(rec);

        // Assert
        await act.Should().NotThrowAsync();
        successChannel.Verify(
            c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchSignal_ChannelFails_RetriesUpToMaxAttempts()
    {
        // Arrange: MaxRetryAttempts=2, 채널이 항상 실패
        // → 2번 시도(attempt 1, 2) 후 포기
        // 재시도 딜레이: attempt 1 실패 후 1s*2^0=1s 대기 → 총 2번 호출
        var failingChannel = CreateChannelMock("Email", isEnabled: true);
        var callCount = 0;
        failingChannel
            .Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new InvalidOperationException("SMTP error"));

        var settings = new NotificationSettings
        {
            MaxRetryAttempts = 2,
            RetryDelaySeconds = 0  // Math.Max(1,0)=1이지만 테스트 목적상 가장 짧게 설정
        };

        var sut = CreateSut(new[] { failingChannel.Object }, settings);
        var rec = CreateRecommendation();

        // Act
        await sut.DispatchSignalAsync(rec);

        // Assert: MaxRetryAttempts=2이므로 정확히 2회 호출
        callCount.Should().Be(2);
    }

    // ══════════════════════════════════════════════════════════════════
    // 2. DispatchAlertAsync 테스트
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchAlert_NoActiveChannels_DoesNothing()
    {
        // Arrange: 채널 없음
        var sut = CreateSut(Enumerable.Empty<INotificationChannel>());

        // Act & Assert: 예외 없이 조용히 종료
        var act = async () => await sut.DispatchAlertAsync("Risk threshold breached");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAlert_ActiveChannel_CallsSendAlert()
    {
        // Arrange
        const string alertMessage = "Daily loss limit exceeded: -3.1%";

        var channel = CreateChannelMock("Telegram", isEnabled: true);
        channel
            .Setup(c => c.SendAlertAsync(alertMessage, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(new[] { channel.Object });

        // Act
        await sut.DispatchAlertAsync(alertMessage);

        // Assert: 동일한 메시지 문자열로 호출
        channel.Verify(
            c => c.SendAlertAsync(
                It.Is<string>(m => m == alertMessage),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchAlert_DisabledChannel_IsNotCalled()
    {
        // Arrange: 활성 채널 1개 + 비활성 채널 1개
        var active = CreateChannelMock("Discord", isEnabled: true);
        active.Setup(c => c.SendAlertAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var disabled = CreateChannelMock("Email", isEnabled: false);

        var sut = CreateSut(new[] { active.Object, disabled.Object });

        // Act
        await sut.DispatchAlertAsync("Test alert");

        // Assert
        active.Verify(c => c.SendAlertAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        disabled.Verify(c => c.SendAlertAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════
    // 3. DispatchDailyReportAsync 테스트
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchDailyReport_ActiveChannel_CallsSendDailyReport()
    {
        // Arrange
        var channel = CreateChannelMock("Discord", isEnabled: true);
        channel
            .Setup(c => c.SendDailyReportAsync(It.IsAny<DailyReportData>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut(new[] { channel.Object });
        var report = CreateDailyReport();

        // Act
        await sut.DispatchDailyReportAsync(report);

        // Assert: 동일한 report 객체 전달 확인
        channel.Verify(
            c => c.SendDailyReportAsync(
                It.Is<DailyReportData>(r => r.ReportDate == report.ReportDate && r.TotalSignals == 5),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DispatchDailyReport_NoActiveChannels_DoesNotThrow()
    {
        // Arrange: 비활성 채널만
        var disabled = CreateChannelMock("Email", isEnabled: false);
        var sut = CreateSut(new[] { disabled.Object });
        var report = CreateDailyReport();

        // Act & Assert
        var act = async () => await sut.DispatchDailyReportAsync(report);
        await act.Should().NotThrowAsync();

        disabled.Verify(
            c => c.SendDailyReportAsync(It.IsAny<DailyReportData>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════
    // 4. TestAllChannelsAsync 테스트
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TestAllChannels_ReturnsResultsForAllChannels()
    {
        // Arrange: 활성 채널 2개
        var telegram = CreateChannelMock("Telegram", isEnabled: true);
        telegram.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var discord = CreateChannelMock("Discord", isEnabled: true);
        discord.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut(new[] { telegram.Object, discord.Object });

        // Act
        var results = await sut.TestAllChannelsAsync();

        // Assert
        results.Should().HaveCount(2);
        results["Telegram"].Should().BeTrue();
        results["Discord"].Should().BeFalse();
    }

    [Fact]
    public async Task TestAllChannels_DisabledChannel_ReturnsFalseWithoutCallingTest()
    {
        // Arrange: 비활성 채널 — TestConnectionAsync를 호출하지 않고 false 반환
        var disabled = CreateChannelMock("Email", isEnabled: false);

        var sut = CreateSut(new[] { disabled.Object });

        // Act
        var results = await sut.TestAllChannelsAsync();

        // Assert: 결과는 false, TestConnectionAsync는 호출되지 않음
        results.Should().ContainKey("Email");
        results["Email"].Should().BeFalse();

        disabled.Verify(
            c => c.TestConnectionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TestAllChannels_ChannelThrows_ReturnsFalse()
    {
        // Arrange: 활성 채널이지만 TestConnectionAsync에서 예외 발생
        var channel = CreateChannelMock("Telegram", isEnabled: true);
        channel
            .Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var sut = CreateSut(new[] { channel.Object });

        // Act
        var results = await sut.TestAllChannelsAsync();

        // Assert: 예외는 삼켜지고 false 반환
        results["Telegram"].Should().BeFalse();
    }

    [Fact]
    public async Task TestAllChannels_MixedChannels_ReturnsCorrectMap()
    {
        // Arrange: 활성+성공, 활성+실패, 비활성 총 3개 채널
        var successChannel = CreateChannelMock("Telegram", isEnabled: true);
        successChannel.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var failChannel = CreateChannelMock("Discord", isEnabled: true);
        failChannel.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var disabledChannel = CreateChannelMock("Email", isEnabled: false);

        var sut = CreateSut(new[] { successChannel.Object, failChannel.Object, disabledChannel.Object });

        // Act
        var results = await sut.TestAllChannelsAsync();

        // Assert
        results.Should().HaveCount(3);
        results["Telegram"].Should().BeTrue();
        results["Discord"].Should().BeFalse();
        results["Email"].Should().BeFalse();

        // 비활성 채널은 TestConnectionAsync 미호출
        disabledChannel.Verify(
            c => c.TestConnectionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════
    // 5. TestChannelAsync 테스트
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TestChannel_NotFound_ReturnsFalse()
    {
        // Arrange: 채널 없음
        var sut = CreateSut(Enumerable.Empty<INotificationChannel>());

        // Act
        var result = await sut.TestChannelAsync("NonExistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestChannel_Disabled_ReturnsFalse()
    {
        // Arrange: 채널은 존재하나 비활성
        var disabled = CreateChannelMock("Telegram", isEnabled: false);
        var sut = CreateSut(new[] { disabled.Object });

        // Act
        var result = await sut.TestChannelAsync("Telegram");

        // Assert: false 반환, TestConnectionAsync 미호출
        result.Should().BeFalse();
        disabled.Verify(
            c => c.TestConnectionAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TestChannel_Enabled_ReturnsTestConnectionResult()
    {
        // Arrange: 활성 채널, TestConnectionAsync → true
        var channel = CreateChannelMock("Discord", isEnabled: true);
        channel.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = CreateSut(new[] { channel.Object });

        // Act
        var result = await sut.TestChannelAsync("Discord");

        // Assert
        result.Should().BeTrue();
        channel.Verify(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TestChannel_Enabled_PropagatesTestConnectionFalse()
    {
        // Arrange: 활성 채널이지만 연결 실패 → false
        var channel = CreateChannelMock("Email", isEnabled: true);
        channel.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var sut = CreateSut(new[] { channel.Object });

        // Act
        var result = await sut.TestChannelAsync("Email");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestChannel_Enabled_ThrowsInternally_ReturnsFalse()
    {
        // Arrange: TestConnectionAsync에서 예외 발생
        var channel = CreateChannelMock("Telegram", isEnabled: true);
        channel
            .Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Connection timed out"));

        var sut = CreateSut(new[] { channel.Object });

        // Act & Assert: 예외가 외부로 전파되지 않고 false 반환
        var act = async () => await sut.TestChannelAsync("Telegram");
        await act.Should().NotThrowAsync();

        var result = await sut.TestChannelAsync("Telegram");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TestChannel_NameComparison_IsCaseInsensitive()
    {
        // Arrange: 채널 이름 "Telegram", 조회 시 "telegram" (소문자)
        var channel = CreateChannelMock("Telegram", isEnabled: true);
        channel.Setup(c => c.TestConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = CreateSut(new[] { channel.Object });

        // Act: 대소문자 무관하게 찾아야 함 (OrdinalIgnoreCase)
        var result = await sut.TestChannelAsync("telegram");

        // Assert
        result.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════
    // 6. GetActiveChannels 필터링 검증 (공통 경계값 테스트)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DispatchSignal_MixedChannels_OnlyCallsEnabledOnes()
    {
        // Arrange: 활성 2개 + 비활성 1개
        var ch1 = CreateChannelMock("Telegram", isEnabled: true);
        ch1.Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);

        var ch2 = CreateChannelMock("Discord", isEnabled: false);

        var ch3 = CreateChannelMock("Email", isEnabled: true);
        ch3.Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
           .Returns(Task.CompletedTask);

        var sut = CreateSut(new[] { ch1.Object, ch2.Object, ch3.Object });

        // Act
        await sut.DispatchSignalAsync(CreateRecommendation());

        // Assert
        ch1.Verify(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()), Times.Once);
        ch2.Verify(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()), Times.Never);
        ch3.Verify(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchSignal_CancellationRequested_DoesNotThrow()
    {
        // Arrange: CancellationToken 이미 취소된 상태
        // ExecuteWithRetryAsync는 OperationCanceledException 조용히 처리
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var channel = CreateChannelMock("Telegram", isEnabled: true);
        channel
            .Setup(c => c.SendSignalAsync(It.IsAny<TradeRecommendation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = CreateSut(new[] { channel.Object });

        // Act & Assert: 취소 예외는 외부로 전파되지 않아야 함
        var act = async () => await sut.DispatchSignalAsync(CreateRecommendation(), cts.Token);
        await act.Should().NotThrowAsync();
    }
}
