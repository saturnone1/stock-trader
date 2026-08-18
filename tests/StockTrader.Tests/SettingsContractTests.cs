using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Settings;

namespace StockTrader.Tests;

public sealed class SettingsContractTests
{
    [Fact]
    public void ResponseExposesSecretPresenceWithoutReturningSecretMaterial()
    {
        var settings = new ManagedSettings
        {
            TelegramBotToken = "telegram-secret-material",
            DiscordWebhookUrl = "https://discord.example/secret-material",
            SmtpPassword = "smtp-secret-material",
            EnabledPatterns = [PatternType.Breakout],
            WatchlistSymbols = ["SPY"]
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var json = JsonSerializer.Serialize(SettingsResponse.Create(settings), options);

        json.Should().Contain("\"telegramBotTokenConfigured\":true");
        json.Should().Contain("\"discordWebhookConfigured\":true");
        json.Should().Contain("\"smtpPasswordConfigured\":true");
        json.Should().NotContain("telegram-secret-material");
        json.Should().NotContain("discord.example");
        json.Should().NotContain("smtp-secret-material");
    }

    [Fact]
    public void ResponseChoicesProjectOnlyImplementedProvidersAndBuiltInPatterns()
    {
        var response = SettingsResponse.Create(new ManagedSettings());

        response.OrderModes.Select(item => item.Code)
            .Should().Equal(OrderModeCatalog.All.Select(item => item.Code));
        response.DataProviders.Select(item => item.Code)
            .Should().BeEquivalentTo(DataProviderCatalog.Implemented.Select(item => item.Value.ToString()));
        response.Patterns.Select(item => item.Code)
            .Should().Equal(PatternCatalog.BuiltIn.Select(item => item.Code));
        response.DataProviders.Should().NotContain(item => item.Code == DataSource.Polygon.ToString());
        response.Patterns.Should().NotContain(item => item.Code == PatternType.Custom.ToString());
    }
}
