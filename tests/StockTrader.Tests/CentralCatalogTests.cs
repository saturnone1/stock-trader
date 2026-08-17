using FluentAssertions;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;
using StockTrader.Api.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockTrader.Tests;

public class CentralCatalogTests
{
    [Fact]
    public void IndicatorCatalogHasUniqueCodesAndCompleteUiMetadata()
    {
        IndicatorCatalog.All.Should().HaveCount(34);
        IndicatorCatalog.All.Select(item => item.Code)
            .Should().OnlyHaveUniqueItems();
        IndicatorCatalog.All.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.Code)
            && !string.IsNullOrWhiteSpace(item.DisplayName)
            && !string.IsNullOrWhiteSpace(item.Category)
            && !string.IsNullOrWhiteSpace(item.DefaultOperator));
        IndicatorCatalog.All.SelectMany(item => item.Parameters)
            .Should().OnlyContain(parameter => parameter.Step > 0 && parameter.DefaultValue > 0);
    }

    [Theory]
    [InlineData("RSI", 16)]
    [InlineData("CUMULATIVE_RSI", 6)]
    [InlineData("MACD_HIST", 37)]
    [InlineData("SMA_SLOPE", 27)]
    [InlineData("ADX", 29)]
    public void RequiredBarsUsesCatalogDefaults(string indicator, int expected)
    {
        IndicatorCatalog.RequiredBars(indicator, null).Should().Be(expected);
    }

    [Fact]
    public void ProviderCatalogCoversEveryEnumAndOnlyExposesImplementedAdapters()
    {
        DataProviderCatalog.All.Select(item => item.Value)
            .Should().BeEquivalentTo(Enum.GetValues<DataSource>());
        DataProviderCatalog.Implemented.Select(item => item.Value)
            .Should().BeEquivalentTo([DataSource.Alpaca, DataSource.Yahoo, DataSource.LsSecurities]);
        DataProviderCatalog.Get(DataSource.Polygon).IsImplemented.Should().BeFalse();
    }

    [Theory]
    [InlineData(DataSource.Yahoo, TimeFrame.OneMinute, 7)]
    [InlineData(DataSource.Yahoo, TimeFrame.FiveMinute, 60)]
    [InlineData(DataSource.Yahoo, TimeFrame.Daily, null)]
    [InlineData(DataSource.LsSecurities, TimeFrame.FifteenMinute, 365)]
    [InlineData(DataSource.Alpaca, TimeFrame.OneMinute, null)]
    public void ProviderLookbackLimitsAreCentralized(DataSource source, TimeFrame timeFrame, int? expectedDays)
    {
        DataProviderCatalog.MaximumLookbackDays(source, timeFrame).Should().Be(expectedDays);
    }

    [Fact]
    public void StrategyBuilderContractIsVersionedAndProjectsEveryCentralCatalogEntry()
    {
        var contract = StrategyBuilderMetadataResponse.Create();

        contract.SchemaVersion.Should().Be(2);
        contract.EntryModes.Select(item => item.Code).Should().BeEquivalentTo(StrategyCatalog.EntryModes.Select(item => item.Code));
        contract.StopMethods.Should().NotBeEmpty();
        contract.LiveStrategyConstraints.SupportedEntryModes.Should().Contain("NextOpen");
        contract.Indicators.Select(item => item.Code)
            .Should().Equal(IndicatorCatalog.All.Select(item => item.Code));
        contract.TimeFrames.Select(item => item.Value)
            .Should().BeEquivalentTo(TimeFrameCatalog.All.Select(item => item.Value));
        contract.DataProviders.Select(item => item.Value)
            .Should().BeEquivalentTo(DataProviderCatalog.Implemented.Select(item => item.Value));
        contract.RuleOperators.Should().Contain(["crosses_above", "crosses_below"]);
        contract.TimeFrames.Should().OnlyContain(item =>
            item.Preview.DefaultLookbackDays > 0
            && item.Preview.MaximumRangeDays >= item.Preview.DefaultLookbackDays
            && item.Preview.SuggestedRangeDays.Count > 0);
    }

    [Fact]
    public void StrategyBuilderContractSerializesEnumsAndProviderLimitsForTheFrontend()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(StrategyBuilderMetadataResponse.Create(), options));
        var root = json.RootElement;
        var yahoo = root.GetProperty("dataProviders").EnumerateArray()
            .Single(item => item.GetProperty("value").GetString() == "Yahoo");

        root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        root.GetProperty("timeFrames")[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        yahoo.GetProperty("maximumLookbackDays").GetProperty("OneMinute").GetInt32().Should().Be(7);
    }
}
