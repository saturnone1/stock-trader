using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Application.Strategies;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class CustomPatternStoreTests
{
    [Fact]
    public async Task UniqueNormalizedNameClosesCreateRaceAndLeavesExistingRowIntact()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new CustomPatternStore(db);

        var first = await store.AddAsync(Strategy("Momentum"));
        var raced = await store.AddAsync(Strategy("  momentum  "));

        first.Result.Should().Be(CustomPatternWriteResult.Saved);
        first.Strategy!.Id.Should().BePositive();
        first.Strategy.Document.StoredStrategyId.Should().Be(first.Strategy.Id);
        raced.Result.Should().Be(CustomPatternWriteResult.NameConflict);
        var stored = await db.CustomPatterns.AsNoTracking().SingleAsync();
        stored.Name.Should().Be("Momentum");
        stored.NormalizedName.Should().Be("MOMENTUM");
    }

    [Fact]
    public async Task UniqueNormalizedNameClosesUpdateRaceAndPreservesStoredDefinition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new CustomPatternStore(db);
        var first = await store.AddAsync(Strategy("Alpha"));
        var second = await store.AddAsync(Strategy("Beta"));

        var update = second.Strategy! with { Document = second.Strategy.Document.Copy() };
        update.Document.Name = "alpha";
        var raced = await store.UpdateAsync(update);

        raced.Result.Should().Be(CustomPatternWriteResult.NameConflict);
        var storedNames = await db.CustomPatterns.AsNoTracking()
            .OrderBy(pattern => pattern.Id)
            .Select(pattern => pattern.Name)
            .ToArrayAsync();
        storedNames.Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public async Task UpdateDeleteRaceReturnsNotFoundInsteadOfLeakingEfConcurrencyException()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        var store = new CustomPatternStore(db);
        var created = await store.AddAsync(Strategy("Deleted"));
        await db.CustomPatterns.ExecuteDeleteAsync();

        var result = await store.UpdateAsync(created.Strategy!);

        result.Result.Should().Be(CustomPatternWriteResult.NotFound);
        result.Strategy.Should().BeNull();
    }

    private static AppDbContext CreateContext(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static StoredStrategy Strategy(string name) => new(
        0,
        new StrategyDocument { Name = name },
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
}
