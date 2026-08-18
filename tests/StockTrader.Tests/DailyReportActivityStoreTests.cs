using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class DailyReportActivityStoreTests
{
    [Fact]
    public async Task ReadAsync_SelectsCompletedTradesByExitTimeAndAllSignalsInHalfOpenWindow()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"DailyReport_{Guid.NewGuid()}")
            .Options;
        await using (var db = new AppDbContext(options))
        {
            db.TradeRecords.AddRange(
                Trade("AAPL", Utc(1, 12), Utc(18, 15)),
                Trade("BEFORE", Utc(1, 12), Utc(18, 3)),
                Trade("BOUNDARY", Utc(1, 12), Utc(19, 4)));
            db.TradeRecommendations.AddRange(
                Signal("AAPL", Utc(18, 12)),
                Signal("MSFT", Utc(19, 3)));
            await db.SaveChangesAsync();
        }
        var store = new DailyReportActivityStore(new TestDbContextFactory(options));

        var result = await store.ReadAsync(Utc(18, 4), Utc(19, 4));

        result.Trades.Select(trade => trade.Symbol).Should().Equal("AAPL");
        result.Signals.Select(signal => signal.Symbol).Should().Equal("MSFT", "AAPL");
    }

    private static TradeRecord Trade(string symbol, DateTime entry, DateTime exit) => new()
    {
        Symbol = symbol,
        PatternType = PatternType.Breakout,
        EntryPrice = 100m,
        ExitPrice = 101m,
        Quantity = 10,
        EntryTime = entry,
        ExitTime = exit,
        PnL = 10m,
        PnLPercent = 0.01m,
        ExitReason = "Test"
    };

    private static TradeRecommendation Signal(string symbol, DateTime generatedAt) => new()
    {
        Symbol = symbol,
        PatternType = PatternType.Breakout,
        GeneratedAt = generatedAt,
        EntryPrice = 100m
    };

    private static DateTime Utc(int day, int hour) =>
        new(2026, 8, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed class TestDbContextFactory(
        DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
