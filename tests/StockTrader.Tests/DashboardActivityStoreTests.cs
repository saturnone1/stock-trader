using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class DashboardActivityStoreTests
{
    [Fact]
    public async Task GetAsync_CountsOnlyActiveSignalsAndRanksRecommendationsDeterministically()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"Dashboard_{Guid.NewGuid()}")
            .Options;
        await using (var db = new AppDbContext(options))
        {
            db.PatternSignals.AddRange(
                Signal(true),
                Signal(true),
                Signal(false),
                Signal(true, superseded: true));
            db.TradeRecommendations.AddRange(
                Recommendation("OLDER", 1, Utc(11)),
                Recommendation("LOW-ID", 2, Utc(12)),
                Recommendation("HIGH-ID", 3, Utc(12)),
                Recommendation("SUPERSEDED", 4, Utc(13), superseded: true));
            await db.SaveChangesAsync();
        }
        var store = new DashboardActivityStore(new TestDbContextFactory(options));

        var result = await store.GetAsync(2);

        result.ActiveSignalCount.Should().Be(2);
        result.RecentRecommendations.Select(item => item.Symbol)
            .Should().Equal("HIGH-ID", "LOW-ID");
        result.RecentRecommendations[0].RiskRewardRatio.Should().Be(2m);
    }

    private static PatternSignal Signal(bool active, bool superseded = false) => new()
    {
        Symbol = Guid.NewGuid().ToString("N"),
        PatternType = PatternType.Breakout,
        IsActive = active,
        IsSuperseded = superseded,
        DetectedAt = Utc(10)
    };

    private static TradeRecommendation Recommendation(
        string symbol,
        long id,
        DateTime generatedAt,
        bool superseded = false) => new()
    {
        Id = id,
        Symbol = symbol,
        PatternType = PatternType.Breakout,
        EntryPrice = 100m,
        StopLossPrice = 97m,
        TargetPrice = 106m,
        GeneratedAt = generatedAt,
        IsSuperseded = superseded
    };

    private static DateTime Utc(int hour) =>
        new(2026, 8, 18, hour, 0, 0, DateTimeKind.Utc);

    private sealed class TestDbContextFactory(
        DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
