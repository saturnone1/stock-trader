using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class SettingsRepositoryTests
{
    [Fact]
    public async Task CacheReturnsIndependentEntitiesAndPublishesOnlySavedChanges()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        db.UserSettings.Add(new UserSettings
        {
            Id = 1,
            WatchlistSymbols = ["SPY"],
            EnabledPatterns = [PatternType.Breakout],
            LastModified = new DateTime(2026, 8, 18, 8, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var clock = new Mock<TimeProvider>();
        var repository = new SettingsRepository(db, new MemoryCache(new MemoryCacheOptions()), clock.Object);

        var first = await repository.GetAsync();
        first.WatchlistSymbols.Add("TQQQ");
        var beforeSave = await repository.GetAsync();

        beforeSave.Should().NotBeSameAs(first);
        beforeSave.WatchlistSymbols.Should().Equal("SPY");

        await repository.SaveAsync(first);
        var afterSave = await repository.GetAsync();

        afterSave.Should().NotBeSameAs(first);
        afterSave.WatchlistSymbols.Should().Equal("SPY", "TQQQ");
        afterSave.EnabledPatterns.Should().NotBeSameAs(first.EnabledPatterns);
    }

    [Fact]
    public async Task FirstReadCreatesDetachedDefaultsThatCanBeSavedInTheSameScope()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new AppDbContext(options);
        var clock = new Mock<TimeProvider>();
        var now = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);
        clock.Setup(item => item.GetUtcNow()).Returns(now);
        var repository = new SettingsRepository(db, new MemoryCache(new MemoryCacheOptions()), clock.Object);

        var settings = await repository.GetAsync();
        settings.WatchlistSymbols = ["TQQQ"];
        await repository.SaveAsync(settings);

        settings.LastModified.Should().Be(now.UtcDateTime);
        (await db.UserSettings.AsNoTracking().SingleAsync()).WatchlistSymbols.Should().Equal("TQQQ");
    }
}
