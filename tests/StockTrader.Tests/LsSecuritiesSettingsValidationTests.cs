using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Extensions;

namespace StockTrader.Tests;

public sealed class LsSecuritiesSettingsValidationTests
{
    [Theory]
    [InlineData("LsSecurities:BaseUrl", "http://insecure.example")]
    [InlineData("LsSecurities:PaperBaseUrl", "")]
    [InlineData("LsSecurities:WebSocketUrl", "https://not-websocket.example")]
    [InlineData("LsSecurities:TokenExpirySafetyMinutes", "-1")]
    public void InvalidOperationalValuesFailValidation(string key, string value)
    {
        var values = ValidValues();
        values[key] = value;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStockTraderServices(configuration, includeHostedServices: false);
        using var provider = services.BuildServiceProvider();

        var readSettings = () => provider
            .GetRequiredService<IOptions<LsSecuritiesSettings>>()
            .Value;

        readSettings.Should().Throw<OptionsValidationException>();
    }

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
        ["LsSecurities:BaseUrl"] = "https://openapi.ls-sec.co.kr:8080",
        ["LsSecurities:PaperBaseUrl"] = "https://openapi.ls-sec.co.kr:29080",
        ["LsSecurities:WebSocketUrl"] = "wss://openapi.ls-sec.co.kr:9443/websocket",
        ["LsSecurities:WebSocketPaperUrl"] = "wss://openapi.ls-sec.co.kr:29443/websocket",
        ["LsSecurities:TokenExpirySafetyMinutes"] = "5",
    };
}
