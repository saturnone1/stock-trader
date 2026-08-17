using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Domain.Strategies;
using StockTrader.Models;

namespace StockTrader.Tests;

public class CompiledStrategyRepositoryTests
{
    [Fact]
    public async Task GetByNamesAsync_ReturnsOnlyValidatedCompiledStrategies()
    {
        await using var db = CreateContext();
        db.CustomPatterns.AddRange(
            ValidPattern("정상 전략"),
            new CustomPatternDefinition
            {
                Name = "손상 전략",
                NormalizedName = StoredStrategyName.Normalize("손상 전략"),
                EntryGroupsJson = "{broken"
            });
        await db.SaveChangesAsync();
        var repository = new CompiledStrategyRepository(db, NullLogger<CompiledStrategyRepository>.Instance);

        var result = await repository.GetByNamesAsync(["정상 전략", "손상 전략"]);

        result.Keys.Should().Equal("정상 전략");
        result["정상 전략"].EntryGroups.Should().ContainSingle();
    }

    [Fact]
    public async Task ListAsync_AppliesActiveAndLiveExecutionBoundary()
    {
        await using var db = CreateContext();
        var live = ValidPattern("실시간 전략");
        live.EnableLiveTrading = true;
        live.EntryMode = "NextOpen";
        var research = ValidPattern("연구 전략");
        var disabled = ValidPattern("비활성 전략");
        disabled.IsActive = false;
        disabled.EnableLiveTrading = true;
        disabled.EntryMode = "NextOpen";
        db.CustomPatterns.AddRange(live, research, disabled);
        await db.SaveChangesAsync();
        var repository = new CompiledStrategyRepository(db, NullLogger<CompiledStrategyRepository>.Instance);

        var result = await repository.ListAsync(activeOnly: true, liveOnly: true);

        result.Select(strategy => strategy.Name).Should().Equal("실시간 전략");
    }

    private static AppDbContext CreateContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static CustomPatternDefinition ValidPattern(string name) => new()
    {
        Name = name,
        NormalizedName = StoredStrategyName.Normalize(name),
        EntryGroupsJson = JsonSerializer.Serialize(new[]
        {
            new ConditionGroup
            {
                Rules =
                [
                    new EntryRule
                    {
                        Indicator = "RSI",
                        Operator = "<=",
                        Value = 30,
                        Params = new() { ["period"] = 14 }
                    }
                ]
            }
        })
    };
}
