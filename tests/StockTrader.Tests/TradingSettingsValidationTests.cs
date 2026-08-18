using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockTrader.Configuration;
using StockTrader.Extensions;

namespace StockTrader.Tests;

public class TradingSettingsValidationTests
{
    [Theory]
    [InlineData("Trading:RiskMonitorMaxConsecutiveFailures", "0")]
    [InlineData("Trading:DataFetchIntervalSeconds", "0")]
    [InlineData("Trading:IntradayDataMaxRetries", "0")]
    [InlineData("Trading:IntradayDataMaxConsecutiveFailures", "0")]
    [InlineData("Trading:IntradayDataCooldownSeconds", "0")]
    [InlineData("Trading:RiskMonitorCooldownSeconds", "0")]
    [InlineData("Trading:RiskHaltAlertIntervalMinutes", "-1")]
    [InlineData("Trading:EntryReconciliationIntervalSeconds", "4")]
    [InlineData("Trading:EntryReconciliationIntervalSeconds", "301")]
    [InlineData("Trading:EntryReconciliationBatchSize", "0")]
    [InlineData("Trading:PatternScanMaxRetries", "0")]
    [InlineData("Trading:PatternScanMaxConsecutiveFailures", "0")]
    [InlineData("Trading:PatternScanCooldownSeconds", "0")]
    [InlineData("Trading:DailyDataSyncIntervalMinutes", "0")]
    [InlineData("Trading:DailyDataSyncCloseDelayMinutes", "-1")]
    [InlineData("Trading:DailyDataSyncMaxRetries", "0")]
    [InlineData("Trading:DailyDataSyncMaxConsecutiveFailures", "0")]
    [InlineData("Trading:DailyDataSyncCooldownSeconds", "0")]
    [InlineData("Trading:PositionMonitoringIntervalSeconds", "0")]
    [InlineData("Trading:PositionOrderResolutionMaxAttempts", "0")]
    [InlineData("Trading:PositionOrderResolutionDelayMilliseconds", "0")]
    [InlineData("Trading:RiskPerTradePercent", "1.1")]
    [InlineData("Trading:MarketOpenET", "not-a-time")]
    public void InvalidOperationalSettingsFailValidation(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                [key] = value
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddStockTraderServices(configuration, includeHostedServices: false);
        using var provider = services.BuildServiceProvider();

        var readSettings = () => provider.GetRequiredService<IOptions<TradingSettings>>().Value;

        readSettings.Should().Throw<OptionsValidationException>();
    }
}
