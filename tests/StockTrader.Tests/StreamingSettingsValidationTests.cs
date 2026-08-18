using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Extensions;

namespace StockTrader.Tests;

public sealed class StreamingSettingsValidationTests
{
    [Theory]
    [InlineData("Streaming:StatusStalenessSeconds", "0")]
    [InlineData("Streaming:BarFlushIntervalSeconds", "0")]
    [InlineData("Streaming:WatchlistSyncIntervalSeconds", "-1")]
    [InlineData("Streaming:BufferCapacity", "0")]
    [InlineData("Streaming:InitialReconnectDelaySeconds", "0")]
    [InlineData("Streaming:MaxReconnectDelaySeconds", "1")]
    public void InvalidStreamingOperationalValuesFailValidation(
        string key,
        string value)
    {
        var values = ValidStreamingValues();
        values[key] = value;
        using var provider = BuildProvider(values);

        var readSettings = () => provider
            .GetRequiredService<IOptions<StreamingSettings>>()
            .Value;

        readSettings.Should().Throw<OptionsValidationException>();
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStockTraderServices(configuration, includeHostedServices: false);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ValidStreamingValues() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
        ["Streaming:MaxReconnectAttempts"] = "10",
        ["Streaming:InitialReconnectDelaySeconds"] = "2",
        ["Streaming:MaxReconnectDelaySeconds"] = "300",
        ["Streaming:StatusStalenessSeconds"] = "180",
        ["Streaming:BarFlushIntervalSeconds"] = "5",
        ["Streaming:WatchlistSyncIntervalSeconds"] = "60",
        ["Streaming:BufferCapacity"] = "10000",
    };
}
