using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using StockTrader.Application.Execution;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Services.Broker;
using StockTrader.Services.Order;

namespace StockTrader.Tests;

public class TradeRepositoryTests
{
    [Fact]
    public async Task TryApplyPositionExitFillAsync_PersistsFullExitAndTradeExactlyOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var position = new Position
        {
            Symbol = "TQQQ",
            Quantity = 2,
            InitialQuantity = 2,
            EntryPrice = 50m,
        };
        await repository.SavePositionAsync(position);
        var requestedAt = new DateTime(2026, 8, 18, 13, 59, 0, DateTimeKind.Utc);
        var claim = new PositionExitClaim(position.Id, requestedAt, "목표 도달", 2, 2);
        (await repository.TryClaimPositionExitAsync(claim)).Should().BeTrue();
        var filledAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var fill = new PositionExitFill(
            position.Id, requestedAt, 2, 2, 55m, filledAt, "exit-1", false);
        var trade = new TradeRecord
        {
            Symbol = "TQQQ",
            EntryPrice = 50m,
            ExitPrice = 55m,
            Quantity = 2,
            ExitReason = "목표 도달",
        };

        (await repository.TryApplyPositionExitFillAsync(fill, trade)).Should().BeTrue();
        (await repository.TryApplyPositionExitFillAsync(fill, trade)).Should().BeFalse();

        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.ClosedAt.Should().Be(filledAt);
        stored.ExitPrice.Should().Be(55m);
        stored.Quantity.Should().Be(2);
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
        var position = new Position { Symbol = "TQQQ", Quantity = 10, InitialQuantity = 10 };
        await repository.SavePositionAsync(position);
        var first = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);

        var firstClaim = new PositionExitClaim(position.Id, first, "손절", 10, 4, true);
        (await repository.TryClaimPositionExitAsync(
            firstClaim with { ExpectedPositionQuantity = 9 })).Should().BeFalse();
        (await repository.TryClaimPositionExitAsync(firstClaim)).Should().BeTrue();
        (await repository.TryClaimPositionExitAsync(firstClaim with { RequestedAt = first.AddSeconds(1) }))
            .Should().BeFalse();

        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.ExitRequestQuantity.Should().Be(4);
        stored.ExitRequestMarksPartialProfit.Should().BeTrue();
    }

    [Fact]
    public async Task TryApplyPositionExitFillAsync_AtomicallyReducesQuantityAndClearsIntent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var position = new Position
        {
            Symbol = "TQQQ",
            Quantity = 10,
            InitialQuantity = 10,
            EntryPrice = 50m,
            CurrentPrice = 54m,
        };
        await repository.SavePositionAsync(position);
        var requestedAt = new DateTime(2026, 8, 18, 13, 59, 0, DateTimeKind.Utc);
        var claim = new PositionExitClaim(position.Id, requestedAt, "1차 이익실현", 10, 4, true);
        (await repository.TryClaimPositionExitAsync(claim)).Should().BeTrue();
        var fill = new PositionExitFill(
            position.Id, requestedAt, 10, 4, 55m, requestedAt.AddMinutes(1), "exit-2", true);
        var trade = new TradeRecord
        {
            Symbol = "TQQQ",
            EntryPrice = 50m,
            ExitPrice = 55m,
            Quantity = 4,
            ExitReason = "1차 이익실현",
        };

        (await repository.TryApplyPositionExitFillAsync(fill, trade)).Should().BeTrue();
        (await repository.TryApplyPositionExitFillAsync(fill, trade)).Should().BeFalse();

        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.Quantity.Should().Be(6);
        stored.InitialQuantity.Should().Be(10);
        stored.CurrentPrice.Should().Be(55m);
        stored.PartialProfitTaken.Should().BeTrue();
        stored.ClosedAt.Should().BeNull();
        stored.ExitRequestedAt.Should().BeNull();
        stored.ExitRequestQuantity.Should().BeNull();
        stored.ExitOrderId.Should().BeNull();
        (await db.TradeRecords.AsNoTracking().SingleAsync()).Quantity.Should().Be(4);
    }

    [Fact]
    public async Task PendingPartialExitSurvivesRestartAndReconcilesExactlyOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var requestedAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);

        await using (var firstDb = new AppDbContext(options))
        {
            await firstDb.Database.EnsureCreatedAsync();
            using var firstCache = new MemoryCache(new MemoryCacheOptions());
            var firstRepository = new TradeRepository(firstDb, firstCache);
            var position = new Position
            {
                Symbol = "TQQQ",
                Quantity = 10,
                InitialQuantity = 10,
                EntryPrice = 50m,
                OpenedAt = requestedAt.AddDays(-2),
            };
            await firstRepository.SavePositionAsync(position);
            var claim = new PositionExitClaim(
                position.Id, requestedAt, "1차 이익실현", 10, 4, true);
            (await firstRepository.TryClaimPositionExitAsync(claim)).Should().BeTrue();
            (await firstRepository.SetPositionExitOrderIdAsync(
                position.Id, requestedAt, "exit-restart")).Should().BeTrue();
        }

        await using (var restartedDb = new AppDbContext(options))
        {
            using var restartedCache = new MemoryCache(new MemoryCacheOptions());
            var restartedRepository = new TradeRepository(restartedDb, restartedCache);
            var restored = (await restartedRepository.GetOpenPositionsAsync()).Single();
            var coordinator = new LivePositionExitCoordinator(
                restartedRepository,
                new FixedTimeProvider(new DateTimeOffset(requestedAt.AddMinutes(1), TimeSpan.Zero)));
            var broker = Mock.Of<IBrokerService>();
            var filledOrder = new BrokerOrder
            {
                OrderId = "exit-restart",
                Symbol = "TQQQ",
                Direction = TradeDirection.Short,
                Quantity = 4,
                FilledQuantity = 4,
                AverageFillPrice = 55m,
                Status = BrokerOrderStatus.Filled,
                SubmittedAt = requestedAt,
                FilledAt = requestedAt.AddMinutes(1),
            };

            var firstResult = await coordinator.ReconcileAsync(restored, broker, [filledOrder]);
            var duplicateResult = await coordinator.ReconcileAsync(restored, broker, [filledOrder]);

            firstResult.Status.Should().Be(LiveExitReconciliationStatus.Completed);
            duplicateResult.Status.Should().Be(LiveExitReconciliationStatus.NotPending);
        }

        await using var verifyDb = new AppDbContext(options);
        var stored = await verifyDb.Positions.AsNoTracking().SingleAsync();
        stored.Quantity.Should().Be(6);
        stored.PartialProfitTaken.Should().BeTrue();
        stored.ExitRequestedAt.Should().BeNull();
        (await verifyDb.TradeRecords.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
