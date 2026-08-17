using FluentAssertions;
using StockTrader.Application.Strategies;
using StockTrader.Data.Repositories;
using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class StoredStrategyMapperTests
{
    [Fact]
    public void PersistenceAdapterRoundTripsEveryDocumentFieldAndServerMetadata()
    {
        var document = new StrategyDocument
        {
            StoredStrategyId = 42,
            DocumentVersion = StrategyDocumentVersions.Current,
            Name = "저장 왕복",
            Description = "all fields",
            EntryRulesJson = "[1]",
            EntryLogic = "OR",
            RequireBullRegime = true,
            AtrStopMultiplier = 1.7m,
            AtrTargetMultiplier = 4.2m,
            MaxHoldingBars = 17,
            TrailingAtr = 1.1m,
            PartialProfitR = 2.3m,
            UseWeightTiers = true,
            WeightTiersJson = "[2]",
            DefaultAllocationPercent = 65m,
            ExitRulesJson = "[3]",
            ExitRulesLogic = "AND",
            ExitGroupsJson = "[4]",
            ExitGroupsLogic = "AND",
            ScalingRulesJson = "[5]",
            TimeFilterJson = "{\"blockedMonths\":[8]}",
            CircuitBreakerJson = "{\"cooldownBars\":7}",
            ReentryJson = "{\"cooldownBarsAfterLoss\":3}",
            PortfolioRulesJson = "{\"maxTotalPositions\":4}",
            EntryGroupsJson = "[6]",
            EntryGroupsLogic = "OR",
            DynamicExitJson = "{\"stopType\":\"SMA\"}",
            EntryMode = StrategyCatalog.NextOpenEntryMode,
            TimeFrame = TimeFrame.Weekly,
            SizingMode = StrategyCatalog.HalfKellySizingMode,
            IsActive = false,
            EnableLiveTrading = true,
        };
        var createdAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updatedAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var stored = new StoredStrategy(42, document, createdAt, updatedAt);

        var roundTrip = stored.ToEntity().ToStoredStrategy();

        roundTrip.Should().BeEquivalentTo(stored);
    }
}
