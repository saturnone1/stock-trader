using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using StockTrader.Api.Contracts;
using StockTrader.Application.Strategies;
using StockTrader.Domain.Strategies;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class CustomPatternContractTests
{
    [Fact]
    public void WriteContractOwnsOnlyClientEditableFieldsAndPreservesDocumentDefaults()
    {
        var json = """
            {
              "id": 999,
              "name": "계약 전략",
              "createdAt": "1999-01-01T00:00:00Z",
              "updatedAt": "1999-01-01T00:00:00Z"
            }
            """;

        var request = JsonSerializer.Deserialize<CustomPatternWriteRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var definition = request.ToDefinition();

        typeof(CustomPatternWriteRequest).GetProperty("Id").Should().BeNull();
        typeof(CustomPatternWriteRequest).GetProperty("CreatedAt").Should().BeNull();
        typeof(CustomPatternWriteRequest).GetProperty("UpdatedAt").Should().BeNull();
        definition.Id.Should().Be(0);
        definition.Name.Should().Be("계약 전략");
        definition.DocumentVersion.Should().Be(StrategyDocumentVersions.Current);
        definition.EntryRulesJson.Should().Be(StrategyDocumentDefaults.EmptyListJson);
        definition.EntryGroupsLogic.Should().Be(StrategyDocumentDefaults.AndLogic);
        definition.ExitGroupsLogic.Should().Be(StrategyDocumentDefaults.OrLogic);
        definition.AtrStopMultiplier.Should().Be(StrategyDocumentDefaults.AtrStopMultiplier);
        definition.AtrTargetMultiplier.Should().Be(StrategyDocumentDefaults.AtrTargetMultiplier);
        definition.EntryMode.Should().Be(StrategyCatalog.CurrentCloseEntryMode);
        definition.SizingMode.Should().Be(StrategyCatalog.FixedRiskSizingMode);
    }

    [Fact]
    public void ContractMapperRoundTripsEveryEditablePropertyWithoutExposingPersistenceOwnership()
    {
        var request = new CustomPatternWriteRequest
        {
            DocumentVersion = 1,
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
        var definition = request.ToDefinition();
        definition.Id = 42;
        definition.CreatedAt = new DateTime(2026, 8, 18, 1, 2, 3, DateTimeKind.Utc);
        definition.UpdatedAt = new DateTime(2026, 8, 18, 4, 5, 6, DateTimeKind.Utc);

        foreach (var property in typeof(CustomPatternWriteRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var entityProperty = typeof(CustomPatternDefinition).GetProperty(property.Name);
            entityProperty.Should().NotBeNull($"the central mapper must own {property.Name}");
            entityProperty!.GetValue(definition).Should().Be(property.GetValue(request), property.Name);
        }

        var response = definition.ToResponse();
        response.Id.Should().Be(42);
        response.CreatedAt.Should().Be(definition.CreatedAt);
        response.UpdatedAt.Should().Be(definition.UpdatedAt);
        response.EntryGroupsJson.Should().Be(request.EntryGroupsJson);
        response.ExitGroupsJson.Should().Be(request.ExitGroupsJson);
        response.TimeFrame.Should().Be(TimeFrame.Weekly);
    }

    [Fact]
    public void ReadContractPreservesTheExistingJsonWireShape()
    {
        var definition = new CustomPatternWriteRequest
        {
            Name = "직렬화 계약",
            EntryGroupsJson = "[{\"label\":\"상황\"}]",
            TimeFrame = TimeFrame.FifteenMinute,
            EnableLiveTrading = true
        }.ToDefinition();
        definition.Id = 17;
        definition.CreatedAt = new DateTime(2026, 8, 18, 1, 0, 0, DateTimeKind.Utc);
        definition.UpdatedAt = new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var persistenceShape = Properties(JsonSerializer.SerializeToElement(definition, options));
        var entityShape = persistenceShape
            .Where(property => property.Key != "normalizedName")
            .ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal);
        var contractShape = Properties(JsonSerializer.SerializeToElement(definition.ToResponse(), options));

        persistenceShape.Should().ContainKey("normalizedName");
        contractShape.Should().NotContainKey("normalizedName");
        contractShape.Should().BeEquivalentTo(entityShape,
            "separating persistence must not silently rename or remove the desktop wire fields");
    }

    [Fact]
    public void ExecutionDocumentMapsEveryStrategyFieldWithoutPersistenceMetadata()
    {
        var stored = new CustomPatternWriteRequest
        {
            Name = "실행 문서",
            Description = "저장 독립",
            EntryGroupsJson = "[1]",
            ExitGroupsJson = "[2]",
            TimeFrame = TimeFrame.Weekly,
            EnableLiveTrading = true
        }.ToDefinition();
        stored.Id = 73;
        stored.NormalizedName = "EXECUTION DOCUMENT";
        stored.CreatedAt = new DateTime(2026, 8, 18, 1, 0, 0, DateTimeKind.Utc);
        stored.UpdatedAt = new DateTime(2026, 8, 18, 2, 0, 0, DateTimeKind.Utc);

        var document = stored.ToStrategyDocument();

        document.StoredStrategyId.Should().Be(73);
        typeof(StrategyDocument).GetProperty("NormalizedName").Should().BeNull();
        typeof(StrategyDocument).GetProperty("CreatedAt").Should().BeNull();
        typeof(StrategyDocument).GetProperty("UpdatedAt").Should().BeNull();
        foreach (var property in typeof(CustomPatternWriteRequest).GetProperties())
        {
            typeof(StrategyDocument).GetProperty(property.Name)!.GetValue(document)
                .Should().Be(typeof(CustomPatternDefinition).GetProperty(property.Name)!.GetValue(stored));
        }

        var target = new CustomPatternDefinition
        {
            Id = 91,
            NormalizedName = "KEEP",
            CreatedAt = stored.CreatedAt,
            UpdatedAt = stored.UpdatedAt
        };
        document.ApplyToStoredDefinition(target);
        target.Id.Should().Be(91);
        target.NormalizedName.Should().Be("KEEP");
        target.CreatedAt.Should().Be(stored.CreatedAt);
        target.UpdatedAt.Should().Be(stored.UpdatedAt);
        target.Name.Should().Be(document.Name);
        target.EntryGroupsJson.Should().Be(document.EntryGroupsJson);
        target.ExitGroupsJson.Should().Be(document.ExitGroupsJson);
    }

    private static IReadOnlyDictionary<string, string> Properties(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetRawText(),
            StringComparer.Ordinal);
}
