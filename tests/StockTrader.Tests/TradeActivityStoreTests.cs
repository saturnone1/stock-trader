using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class TradeActivityStoreTests
{
    [Fact]
    public async Task RecommendationsAreProjectedInNewestFirstOrder()
    {
        var options = Options();
        await using (var db = new AppDbContext(options))
        {
            db.TradeRecommendations.AddRange(
                Recommendation(1, "OLD", Utc(1)),
                Recommendation(2, "TIE-LOW", Utc(2)),
                Recommendation(3, "NEW", Utc(2), "order-2"));
            await db.SaveChangesAsync();
        }
        var store = new TradeActivityStore(new TestDbContextFactory(options));

        var rows = await store.GetRecommendationsAsync(1);

        rows.Should().ContainSingle();
        rows[0].Symbol.Should().Be("NEW");
        rows[0].HasEntryOrderId.Should().BeTrue();
    }

    [Fact]
    public async Task HistoryFiltersCountsAndPagesTheSameDataset()
    {
        var options = Options();
        await using (var db = new AppDbContext(options))
        {
            db.TradeRecords.AddRange(
                Trade(1, PatternType.Breakout, Utc(1), Utc(2)),
                Trade(2, PatternType.Breakout, Utc(3), Utc(4)),
                Trade(3, PatternType.TrendPullback, Utc(5), Utc(6)));
            await db.SaveChangesAsync();
        }
        var store = new TradeActivityStore(new TestDbContextFactory(options));

        var result = await store.GetHistoryAsync(
            PatternType.Breakout, Utc(1), Utc(10), skip: 1, take: 1);

        result.TotalCount.Should().Be(2);
        result.Trades.Should().ContainSingle();
        result.Trades[0].Id.Should().Be(1);
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"TradeActivity_{Guid.NewGuid()}")
            .Options;

    private static TradeRecommendation Recommendation(
        long id,
        string symbol,
        DateTime generatedAt,
        string? orderId = null) => new()
    {
        Id = id,
        Symbol = symbol,
        PatternType = PatternType.Breakout,
        GeneratedAt = generatedAt,
        EntryOrderId = orderId
    };

    private static TradeRecord Trade(
        long id,
        PatternType pattern,
        DateTime entry,
        DateTime exit) => new()
    {
        Id = id,
        Symbol = $"S{id}",
        PatternType = pattern,
        EntryTime = entry,
        ExitTime = exit
    };

    private static DateTime Utc(int day) =>
        new(2026, 8, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class TestDbContextFactory(
        DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
