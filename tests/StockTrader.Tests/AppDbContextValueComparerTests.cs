using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public class AppDbContextValueComparerTests
{
    [Fact]
    public async Task UserSettingsCollections_DetectInPlaceMutations()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        db.UserSettings.Add(new UserSettings
        {
            EnabledPatterns = [PatternType.Breakout],
            WatchlistSymbols = ["SPY"]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var settings = await db.UserSettings.SingleAsync();
        settings.EnabledPatterns.Add(PatternType.VwapReversion);
        settings.WatchlistSymbols.Add("TQQQ");
        db.ChangeTracker.DetectChanges();

        var entry = db.Entry(settings);
        entry.Property(item => item.EnabledPatterns).IsModified.Should().BeTrue();
        entry.Property(item => item.WatchlistSymbols).IsModified.Should().BeTrue();
    }

    [Fact]
    public async Task SymbolProfilePatterns_DetectInPlaceMutation()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        db.SymbolProfiles.Add(new SymbolProfile
        {
            Symbol = "TQQQ",
            Name = "기본",
            EnabledPatterns = [PatternType.Breakout]
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var profile = await db.SymbolProfiles.SingleAsync();
        profile.EnabledPatterns.Add(PatternType.CumulativeRsi2);
        db.ChangeTracker.DetectChanges();

        db.Entry(profile).Property(item => item.EnabledPatterns).IsModified.Should().BeTrue();
    }
}
