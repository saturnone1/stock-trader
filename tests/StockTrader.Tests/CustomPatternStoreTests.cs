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

        var first = await store.AddAsync(new CustomPatternDefinition { Name = "Momentum" });
        var raced = await store.AddAsync(new CustomPatternDefinition { Name = "  momentum  " });

        first.Should().Be(CustomPatternWriteResult.Saved);
        raced.Should().Be(CustomPatternWriteResult.NameConflict);
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
        var first = new CustomPatternDefinition { Name = "Alpha" };
        var second = new CustomPatternDefinition { Name = "Beta" };
        await store.AddAsync(first);
        await store.AddAsync(second);

        second.Name = "alpha";
        var raced = await store.UpdateAsync(second);

        raced.Should().Be(CustomPatternWriteResult.NameConflict);
        var storedNames = await db.CustomPatterns.AsNoTracking()
            .OrderBy(pattern => pattern.Id)
            .Select(pattern => pattern.Name)
            .ToArrayAsync();
        storedNames.Should().Equal("Alpha", "Beta");
    }

    private static AppDbContext CreateContext(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
}
