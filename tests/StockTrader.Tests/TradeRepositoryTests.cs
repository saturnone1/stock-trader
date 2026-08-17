using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public class TradeRepositoryTests
{
    [Fact]
    public async Task TryCompletePositionExitAsync_PersistsPositionAndTradeExactlyOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var position = new Position { Symbol = "TQQQ", Quantity = 2, EntryPrice = 50m };
        await repository.SavePositionAsync(position);
        var requestedAt = new DateTime(2026, 8, 18, 13, 59, 0, DateTimeKind.Utc);
        (await repository.TryClaimPositionExitAsync(position.Id, requestedAt, "목표 도달")).Should().BeTrue();
        position.ExitRequestedAt = requestedAt;
        position.ExitRequestReason = "목표 도달";
        position.ClosedAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        position.ExitPrice = 55m;
        var trade = new TradeRecord
        {
            Symbol = "TQQQ",
            EntryPrice = 50m,
            ExitPrice = 55m,
            Quantity = 2,
            ExitReason = "목표 도달",
        };

        (await repository.TryCompletePositionExitAsync(position, trade)).Should().BeTrue();
        (await repository.TryCompletePositionExitAsync(position, trade)).Should().BeFalse();

        (await db.Positions.AsNoTracking().SingleAsync()).ClosedAt.Should().NotBeNull();
        (await db.TradeRecords.AsNoTracking().SingleAsync()).ExitPrice.Should().Be(55m);
    }

    [Fact]
    public async Task TryClaimPositionExitAsync_AllowsOnlyOneWorkerToOwnExit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var position = new Position { Symbol = "TQQQ" };
        await repository.SavePositionAsync(position);
        var first = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);

        (await repository.TryClaimPositionExitAsync(position.Id, first, "손절")).Should().BeTrue();
        (await repository.TryClaimPositionExitAsync(position.Id, first.AddSeconds(1), "손절"))
            .Should().BeFalse();
    }
}
