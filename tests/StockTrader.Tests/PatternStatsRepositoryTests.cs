using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class PatternStatsRepositoryTests
{
    [Fact]
    public async Task SaveBatchPreservesApplicationOwnedTimestampOnInsertAndUpdate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new PatternStatsRepository(db);
        var firstAt = new DateTime(2026, 8, 19, 7, 0, 0, DateTimeKind.Utc);
        var secondAt = firstAt.AddHours(1);

        await repository.SaveBatchAsync([Stats(1, firstAt)]);
        (await repository.GetAsync(PatternType.Breakout))!.LastUpdated.Should().Be(firstAt);

        await repository.SaveBatchAsync([Stats(2, secondAt)]);
        var updated = await repository.GetAsync(PatternType.Breakout);
        updated!.SampleSize.Should().Be(2);
        updated.LastUpdated.Should().Be(secondAt);
    }

    private static PatternStats Stats(int sampleSize, DateTime updatedAt) => new()
    {
        PatternType = PatternType.Breakout,
        SampleSize = sampleSize,
        LastUpdated = updatedAt
    };
}
