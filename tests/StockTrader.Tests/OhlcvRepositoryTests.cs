using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class OhlcvRepositoryTests
{
    [Fact]
    public async Task AddBarsAsyncReplacesAnEarlierSampleForTheSameBarIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var repository = new OhlcvRepository(db);
        var timestamp = new DateTime(2026, 8, 18, 13, 30, 0, DateTimeKind.Utc);

        await repository.AddBarsAsync([
            Bar(timestamp, close: 100m, high: 101m, volume: 10)
        ]);
        await repository.AddBarsAsync([
            Bar(timestamp, close: 105m, high: 106m, volume: 25)
        ]);

        var stored = await db.OhlcvBars.AsNoTracking().SingleAsync();
        stored.Close.Should().Be(105m);
        stored.High.Should().Be(106m);
        stored.Volume.Should().Be(25);
    }

    private static OhlcvBar Bar(
        DateTime timestamp,
        decimal close,
        decimal high,
        long volume) => new()
    {
        Symbol = "AAPL",
        TimeFrame = TimeFrame.OneMinute,
        Timestamp = timestamp,
        Open = 99m,
        High = high,
        Low = 98m,
        Close = close,
        Volume = volume
    };
}
