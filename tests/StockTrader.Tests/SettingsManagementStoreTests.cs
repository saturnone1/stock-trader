using FluentAssertions;
using Moq;
using StockTrader.Application.Settings;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class SettingsManagementStoreTests
{
    [Fact]
    public async Task AdapterRoundTripsEveryManagedSettingWithoutSharingCollections()
    {
        var entity = new UserSettings
        {
            Id = 7,
            OrderMode = OrderMode.AlertOnly,
            PreferredDataSource = DataSource.Alpaca,
            EnabledPatterns = [PatternType.Breakout],
            WatchlistSymbols = ["SPY"],
            SoundAlerts = true,
            AccountSize = 100_000m,
            RiskPerTradePercent = 0.01m,
            DailyLossLimitPercent = 0.03m,
            MaxTotalPositions = 7,
            MaxPositionsPerSector = 2,
            MinExpectancy = 0.1m,
            LiveParameterOverridesJson = "live-json",
            EnableTelegram = true,
            TelegramBotToken = "telegram",
            TelegramChatId = "chat",
            EnableDiscord = true,
            DiscordWebhookUrl = "discord",
            EnableEmail = true,
            SmtpHost = "smtp",
            SmtpPort = 587,
            SmtpUseSsl = true,
            SmtpUsername = "user",
            SmtpPassword = "password",
            EmailFrom = "from@example.com",
            EmailTo = "to@example.com",
            DailyReportTimeKst = "07:30",
            Tqqq200SmaAllowedSymbols = "TQQQ",
            LastModified = new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc)
        };
        var repository = new Mock<ISettingsRepository>();
        repository.Setup(item => item.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        var store = new SettingsManagementStore(repository.Object);

        var snapshot = await store.GetAsync();
        var updated = snapshot with
        {
            OrderMode = OrderMode.AutoOrder,
            PreferredDataSource = DataSource.Yahoo,
            EnabledPatterns = [PatternType.Tqqq200Sma],
            WatchlistSymbols = ["TQQQ", "QQQ"],
            TelegramBotToken = "new-telegram",
            DiscordWebhookUrl = null,
            SmtpPassword = "new-password",
            LastModified = snapshot.LastModified.AddMinutes(1)
        };
        await store.SaveAsync(updated);

        snapshot.EnabledPatterns.Should().NotBeSameAs(entity.EnabledPatterns);
        snapshot.WatchlistSymbols.Should().NotBeSameAs(entity.WatchlistSymbols);
        entity.OrderMode.Should().Be(OrderMode.AutoOrder);
        entity.PreferredDataSource.Should().Be(DataSource.Yahoo);
        entity.EnabledPatterns.Should().Equal(PatternType.Tqqq200Sma);
        entity.WatchlistSymbols.Should().Equal("TQQQ", "QQQ");
        entity.TelegramBotToken.Should().Be("new-telegram");
        entity.DiscordWebhookUrl.Should().BeNull();
        entity.SmtpPassword.Should().Be("new-password");
        entity.LastModified.Should().Be(updated.LastModified);
        repository.Verify(item => item.SaveAsync(entity, It.IsAny<CancellationToken>()), Times.Once);
    }
}
