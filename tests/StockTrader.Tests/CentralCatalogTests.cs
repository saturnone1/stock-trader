using FluentAssertions;
using StockTrader.Domain.Backtesting;
using StockTrader.Domain.MarketData;
using StockTrader.Domain.Optimization;
using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;
using StockTrader.Api.Contracts;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockTrader.Tests;

public class CentralCatalogTests
{
    [Fact]
    public void MarketDataIdentityPreservesPersistedEnumValues()
    {
        Enum.GetValues<TimeFrame>().Select(value => (value, (int)value)).Should().Equal(
            (TimeFrame.OneMinute, 0),
            (TimeFrame.FiveMinute, 1),
            (TimeFrame.FifteenMinute, 2),
            (TimeFrame.Daily, 3),
            (TimeFrame.Weekly, 4));
        Enum.GetValues<DataSource>().Select(value => (value, (int)value)).Should().Equal(
            (DataSource.Alpaca, 0),
            (DataSource.Polygon, 1),
            (DataSource.Yahoo, 2),
            (DataSource.LsSecurities, 3));
    }

    [Fact]
    public void OrderModeCatalogPreservesPersistedIdentityAndHasInvestorFacingMetadata()
    {
        Enum.GetValues<OrderMode>().Select(value => (value, (int)value)).Should().Equal(
            (OrderMode.AlertOnly, 0),
            (OrderMode.AutoOrder, 1));
        OrderModeCatalog.All.Select(item => item.Value)
            .Should().Equal(Enum.GetValues<OrderMode>());
        OrderModeCatalog.All.Should().OnlyContain(item =>
            item.Code == item.Value.ToString()
            && !string.IsNullOrWhiteSpace(item.DisplayName)
            && !string.IsNullOrWhiteSpace(item.Description));
    }

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

    [Fact]
    public void PatternCatalogPreservesStableIdentityAndCoversEveryPattern()
    {
        PatternCatalog.All.Select(item => item.Value)
            .Should().BeEquivalentTo(Enum.GetValues<PatternType>());
        PatternCatalog.All.Select(item => item.Code)
            .Should().Equal(PatternCatalog.All.Select(item => item.Value.ToString()));
        PatternCatalog.All.Select(item => item.Code).Should().OnlyHaveUniqueItems();
        PatternCatalog.All.Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.DisplayName));
        PatternCatalog.BuiltIn.Select(item => item.Value)
            .Should().BeEquivalentTo(Enum.GetValues<PatternType>().Where(item => item != PatternType.Custom));
        PatternCatalog.DisplayName(PatternType.Custom, "  내 전략  ").Should().Be("내 전략");
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
        DataProviderCatalog.All.Should().OnlyContain(item =>
            !string.IsNullOrWhiteSpace(item.RegimeBenchmarkSymbol));
    }

    [Theory]
    [InlineData(DataSource.Alpaca, DataProviderCatalog.UnitedStatesRegimeBenchmark)]
    [InlineData(DataSource.Yahoo, DataProviderCatalog.UnitedStatesRegimeBenchmark)]
    [InlineData(DataSource.Polygon, DataProviderCatalog.UnitedStatesRegimeBenchmark)]
    [InlineData(DataSource.LsSecurities, DataProviderCatalog.KoreaRegimeBenchmark)]
    public void ProviderRegimeBenchmarksAreCentralized(DataSource source, string expectedSymbol)
    {
        DataProviderCatalog.RegimeBenchmarkSymbol(source).Should().Be(expectedSymbol);
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

        contract.SchemaVersion.Should().Be(5);
        contract.DocumentVersion.Should().Be(StrategyDocumentVersions.Current);
        contract.EntryModes.Select(item => item.Code).Should().BeEquivalentTo(StrategyCatalog.EntryModes.Select(item => item.Code));
        contract.StopMethods.Should().NotBeEmpty();
        contract.LiveStrategyConstraints.SupportedEntryModes.Should().Contain("NextOpen");
        contract.LiveStrategyConstraints.SupportsPartialExit.Should().BeTrue();
        contract.Indicators.Select(item => item.Code)
            .Should().Equal(IndicatorCatalog.All.Select(item => item.Code));
        contract.TimeFrames.Select(item => item.Value)
            .Should().BeEquivalentTo(TimeFrameCatalog.All.Select(item => item.Value));
        contract.DataProviders.Select(item => item.Value)
            .Should().BeEquivalentTo(DataProviderCatalog.Implemented.Select(item => item.Value));
        contract.Patterns.Select(item => item.Value)
            .Should().Equal(PatternCatalog.All.Select(item => item.Value));
        contract.RuleOperators.Should().Contain(["crosses_above", "crosses_below"]);
        contract.SlippageModels.Select(item => item.Value)
            .Should().BeEquivalentTo(Enum.GetValues<SlippageModel>());
        contract.SlippageModels.Should().ContainSingle(item => item.IsDefault)
            .Which.Value.Should().Be(BacktestExecutionCatalog.DefaultSlippageModel);
        contract.OptimizationRankings.Select(item => item.Code)
            .Should().Equal(OptimizationRankingCatalog.All.Select(item => item.Code));
        contract.OptimizationRankings.Should().ContainSingle(item => item.IsDefault)
            .Which.Code.Should().Be(OptimizationRankingCatalog.DefaultCode);
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

        root.GetProperty("schemaVersion").GetInt32().Should().Be(5);
        root.GetProperty("timeFrames")[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        yahoo.GetProperty("maximumLookbackDays").GetProperty("OneMinute").GetInt32().Should().Be(7);
        root.GetProperty("slippageModels")[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("patterns")[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("optimizationRankings")[0].GetProperty("code").GetString()
            .Should().Be(OptimizationRankingCatalog.SortinoRatioCode);
    }
}
