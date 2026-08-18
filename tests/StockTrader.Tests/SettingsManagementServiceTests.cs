using FluentAssertions;
using Moq;
using StockTrader.Application.Settings;

namespace StockTrader.Tests;

public sealed class SettingsManagementServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateNormalizesSymbolsUsesInjectedClockAndPreservesOmittedSecrets()
    {
        var store = new MemoryStore(Current() with
        {
            TelegramBotToken = "telegram-secret",
            DiscordWebhookUrl = "discord-secret",
            SmtpPassword = "smtp-secret"
        });
        var clock = new Mock<TimeProvider>();
        clock.Setup(item => item.GetUtcNow()).Returns(Now);
        var service = new SettingsManagementService(store, clock.Object);
        var command = ValidCommand() with
        {
            WatchlistSymbols = [" tqqq, SPY ", "tqqq", "069500"],
            EnabledPatterns = [PatternType.Breakout, PatternType.Breakout]
        };

        var result = await service.UpdateAsync(command);

        result.Succeeded.Should().BeTrue();
        result.Settings!.WatchlistSymbols.Should().Equal("TQQQ", "SPY", "069500");
        result.Settings.EnabledPatterns.Should().Equal(PatternType.Breakout);
        result.Settings.LastModified.Should().Be(Now.UtcDateTime);
        result.Settings.TelegramBotToken.Should().Be("telegram-secret");
        result.Settings.DiscordWebhookUrl.Should().Be("discord-secret");
        result.Settings.SmtpPassword.Should().Be("smtp-secret");
        store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateAllowsNoBuiltInPatternsToDisableBuiltInScanning()
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);

        var result = await service.UpdateAsync(ValidCommand() with { EnabledPatterns = [] });

        result.Succeeded.Should().BeTrue();
        result.Settings!.EnabledPatterns.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PatternType.OpeningRangeBreakout)]
    [InlineData(PatternType.EarningsDrift)]
    public async Task UpdateRejectsBuiltInPatternsWithoutOperationalSemantics(PatternType patternType)
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);

        var result = await service.UpdateAsync(ValidCommand() with { EnabledPatterns = [patternType] });

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("지원되는 내장 전략"));
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task InvalidTradingRiskAndCatalogValuesFailBeforePersistence()
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);
        var command = ValidCommand() with
        {
            OrderMode = (OrderMode)999,
            PreferredDataSource = DataSource.Polygon,
            EnabledPatterns = [PatternType.Custom],
            RiskPerTradePercent = 1.01m,
            MaxTotalPositions = 2,
            MaxPositionsPerSector = 3,
            WatchlistSymbols = ["BAD SYMBOL"]
        };

        var result = await service.UpdateAsync(command);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThanOrEqualTo(6);
        store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task NullCollectionsFromHostileJsonFailClosed()
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);
        var command = ValidCommand() with
        {
            EnabledPatterns = null!,
            WatchlistSymbols = null!
        };

        var result = await service.UpdateAsync(command);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("전략 목록"));
        result.Errors.Should().Contain(error => error.Contains("관심종목 목록"));
        store.SaveCount.Should().Be(0);
    }

    [Theory]
    [InlineData("7:30")]
    [InlineData("24:00")]
    [InlineData("morning")]
    public async Task InvalidDailyReportTimeFailsClosed(string value)
    {
        var store = new MemoryStore(Current());
        var service = CreateService(store);

        var result = await service.UpdateAsync(ValidCommand() with { DailyReportTimeKst = value });

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("HH:mm"));
        store.SaveCount.Should().Be(0);
    }

    private static SettingsManagementService CreateService(MemoryStore store)
    {
        var clock = new Mock<TimeProvider>();
        clock.Setup(item => item.GetUtcNow()).Returns(Now);
        return new SettingsManagementService(store, clock.Object);
    }

    private static SettingsUpdateCommand ValidCommand() => new()
    {
        OrderMode = OrderMode.AlertOnly,
        PreferredDataSource = DataSource.Alpaca,
        EnabledPatterns = [PatternType.Breakout],
        WatchlistSymbols = ["TQQQ", "SPY"],
        SoundAlerts = true,
        AccountSize = 100_000m,
        RiskPerTradePercent = 0.01m,
        DailyLossLimitPercent = 0.03m,
        MaxTotalPositions = 7,
        MaxPositionsPerSector = 2,
        MinExpectancy = 0m
    };

    private static ManagedSettings Current() => new()
    {
        Id = 1,
        OrderMode = OrderMode.AlertOnly,
        PreferredDataSource = DataSource.Alpaca,
        EnabledPatterns = [PatternType.Breakout],
        WatchlistSymbols = ["SPY"],
        SoundAlerts = true,
        AccountSize = 100_000m,
        RiskPerTradePercent = 0.01m,
        DailyLossLimitPercent = 0.03m,
        MaxTotalPositions = 7,
        MaxPositionsPerSector = 2
    };

    private sealed class MemoryStore(ManagedSettings value) : ISettingsManagementStore
    {
        public ManagedSettings Value { get; private set; } = value;
        public int SaveCount { get; private set; }

        public Task<ManagedSettings> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(Value);

        public Task SaveAsync(ManagedSettings settings, CancellationToken ct = default)
        {
            Value = settings;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
