using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class CustomPatternContractTests
{
    [Fact]
    public void WriteContractOwnsOnlyEditableFieldsAndPreservesDocumentDefaults()
    {
        const string json = """
            {
              "id": 999,
              "name": "계약 전략",
              "createdAt": "1999-01-01T00:00:00Z",
              "updatedAt": "1999-01-01T00:00:00Z"
            }
            """;

        var request = JsonSerializer.Deserialize<CustomPatternWriteRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var document = request.ToStrategyDocument();

        typeof(CustomPatternWriteRequest).GetProperty("Id").Should().BeNull();
        typeof(CustomPatternWriteRequest).GetProperty("CreatedAt").Should().BeNull();
        typeof(CustomPatternWriteRequest).GetProperty("UpdatedAt").Should().BeNull();
        document.StoredStrategyId.Should().BeNull();
        document.Name.Should().Be("계약 전략");
        document.DocumentVersion.Should().Be(StrategyDocumentVersions.Current);
        document.EntryRulesJson.Should().Be(StrategyDocumentDefaults.EmptyListJson);
        document.EntryGroupsLogic.Should().Be(StrategyDocumentDefaults.AndLogic);
        document.ExitGroupsLogic.Should().Be(StrategyDocumentDefaults.OrLogic);
        document.AtrStopMultiplier.Should().Be(StrategyDocumentDefaults.AtrStopMultiplier);
        document.AtrTargetMultiplier.Should().Be(StrategyDocumentDefaults.AtrTargetMultiplier);
        document.EntryMode.Should().Be(StrategyCatalog.CurrentCloseEntryMode);
        document.SizingMode.Should().Be(StrategyCatalog.FixedRiskSizingMode);
    }

    [Fact]
    public void ContractMapperRoundTripsEveryEditablePropertyWithoutStorageTypes()
    {
        var request = CompleteRequest();
        var document = request.ToStrategyDocument();

        foreach (var property in typeof(CustomPatternWriteRequest)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var documentProperty = typeof(StrategyDocument).GetProperty(property.Name);
            documentProperty.Should().NotBeNull($"the HTTP mapper must own {property.Name}");
            documentProperty!.GetValue(document).Should().Be(property.GetValue(request), property.Name);
        }

        var createdAt = new DateTime(2026, 8, 18, 1, 2, 3, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 8, 18, 4, 5, 6, DateTimeKind.Utc);
        var response = new StoredStrategy(42, document, createdAt, updatedAt).ToResponse();
        response.Id.Should().Be(42);
        response.CreatedAt.Should().Be(createdAt);
        response.UpdatedAt.Should().Be(updatedAt);
        response.EntryGroupsJson.Should().Be(request.EntryGroupsJson);
        response.ExitGroupsJson.Should().Be(request.ExitGroupsJson);
        response.TimeFrame.Should().Be(TimeFrame.Weekly);
    }

    [Fact]
    public void ReadContractRemainsFlatAndHidesInternalDocumentReference()
    {
        var stored = new StoredStrategy(
            17,
            CompleteRequest().ToStrategyDocument(),
            new DateTime(2026, 8, 18, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc));
        var json = JsonSerializer.SerializeToElement(
            stored.ToResponse(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var shape = json.EnumerateObject().Select(property => property.Name).ToArray();

        shape.Should().Contain(["id", "name", "createdAt", "updatedAt", "entryGroupsJson"]);
        shape.Should().NotContain("document");
        shape.Should().NotContain("storedStrategyId");
        shape.Should().NotContain("normalizedName");
    }

    private static CustomPatternWriteRequest CompleteRequest() => new()
    {
        DocumentVersion = StrategyDocumentVersions.Current,
        Name = "전체 계약",
        Description = "설명",
        EntryRulesJson = "[1]",
        EntryLogic = "OR",
        RequireBullRegime = true,
        AtrStopMultiplier = 1.5m,
        AtrTargetMultiplier = 4.5m,
        MaxHoldingBars = 27,
        TrailingAtr = 1.2m,
        PartialProfitR = 2.2m,
        UseWeightTiers = true,
        WeightTiersJson = "[2]",
        DefaultAllocationPercent = 63m,
        ExitRulesJson = "[3]",
        ExitRulesLogic = "AND",
        ExitGroupsJson = "[4]",
        ExitGroupsLogic = "AND",
        ScalingRulesJson = "[5]",
        TimeFilterJson = "{\"a\":1}",
        CircuitBreakerJson = "{\"b\":2}",
        ReentryJson = "{\"c\":3}",
        PortfolioRulesJson = "{\"d\":4}",
        EntryGroupsJson = "[6]",
        EntryGroupsLogic = "OR",
        DynamicExitJson = "{\"e\":5}",
        EntryMode = StrategyCatalog.NextOpenEntryMode,
        TimeFrame = TimeFrame.Weekly,
        SizingMode = StrategyCatalog.HalfKellySizingMode,
        IsActive = false,
        EnableLiveTrading = true
    };
}
