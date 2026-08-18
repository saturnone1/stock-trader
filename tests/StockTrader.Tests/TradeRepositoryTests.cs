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
    public async Task TryApplyPositionExecutionFillAsync_PersistsFullExitAndTradeExactlyOnce()
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
        var claim = new PositionExecutionClaim(position.Id, requestedAt, "목표 도달", 2, 2);
        (await repository.TryClaimPositionExecutionAsync(claim)).Should().BeTrue();
        var filledAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var fill = new PositionExecutionFill(
            position.Id, requestedAt, 2, 2, 55m, filledAt, "exit-1");
        var trade = new TradeRecord
        {
            Symbol = "TQQQ",
            EntryPrice = 50m,
            ExitPrice = 55m,
            Quantity = 2,
            ExitReason = "목표 도달",
        };

        (await repository.TryApplyPositionExecutionFillAsync(fill, trade)).Should().BeTrue();
        (await repository.TryApplyPositionExecutionFillAsync(fill, trade)).Should().BeFalse();

        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.ClosedAt.Should().Be(filledAt);
        stored.ExitPrice.Should().Be(55m);
        stored.Quantity.Should().Be(2);
        (await db.TradeRecords.AsNoTracking().SingleAsync()).ExitPrice.Should().Be(55m);
    }

    [Fact]
    public async Task TryClaimPositionExecutionAsync_AllowsOnlyOneWorkerToOwnExecution()
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

        var firstClaim = new PositionExecutionClaim(
            position.Id, first, "1차 이익실현", 10, 4,
            PositionExecutionKind.PartialProfit, MarksPartialProfit: true);
        (await repository.TryClaimPositionExecutionAsync(
            firstClaim with { ExpectedPositionQuantity = 9 })).Should().BeFalse();
        (await repository.TryClaimPositionExecutionAsync(firstClaim)).Should().BeTrue();
        (await repository.TryClaimPositionExecutionAsync(
            firstClaim with { RequestedAt = first.AddSeconds(1) }))
            .Should().BeFalse();

        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.ExecutionRequestQuantity.Should().Be(4);
        stored.ExecutionRequestMarksPartialProfit.Should().BeTrue();
    }

    [Fact]
    public async Task TryApplyPositionExecutionFillAsync_AtomicallyReducesQuantityAndClearsIntent()
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
            StopLossPrice = 45m,
        };
        await repository.SavePositionAsync(position);
        var requestedAt = new DateTime(2026, 8, 18, 13, 59, 0, DateTimeKind.Utc);
        var claim = new PositionExecutionClaim(
            position.Id, requestedAt, "1차 이익실현", 10, 4,
            PositionExecutionKind.PartialProfit, MarksPartialProfit: true);
        (await repository.TryClaimPositionExecutionAsync(claim)).Should().BeTrue();
        var fill = new PositionExecutionFill(
            position.Id, requestedAt, 10, 4, 55m, requestedAt.AddMinutes(1), "exit-2",
            PositionExecutionKind.PartialProfit, MarksPartialProfit: true);
        var trade = new TradeRecord
        {
            Symbol = "TQQQ",
            EntryPrice = 50m,
            ExitPrice = 55m,
            Quantity = 4,
            ExitReason = "1차 이익실현",
        };

        (await repository.TryApplyPositionExecutionFillAsync(fill, trade)).Should().BeTrue();
        (await repository.TryApplyPositionExecutionFillAsync(fill, trade)).Should().BeFalse();

        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.Quantity.Should().Be(6);
        stored.InitialQuantity.Should().Be(10);
        stored.CurrentPrice.Should().Be(55m);
        stored.PartialProfitTaken.Should().BeTrue();
        stored.StopLossPrice.Should().Be(50m);
        stored.BreakevenApplied.Should().BeTrue();
        stored.ClosedAt.Should().BeNull();
        stored.ExecutionRequestedAt.Should().BeNull();
        stored.ExecutionRequestQuantity.Should().BeNull();
        stored.ExecutionOrderId.Should().BeNull();
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
            var claim = new PositionExecutionClaim(
                position.Id, requestedAt, "1차 이익실현", 10, 4,
                PositionExecutionKind.PartialProfit, MarksPartialProfit: true);
            (await firstRepository.TryClaimPositionExecutionAsync(claim)).Should().BeTrue();
            (await firstRepository.SetPositionExecutionOrderIdAsync(
                position.Id, requestedAt, "exit-restart")).Should().BeTrue();
        }

        await using (var restartedDb = new AppDbContext(options))
        {
            using var restartedCache = new MemoryCache(new MemoryCacheOptions());
            var restartedRepository = new TradeRepository(restartedDb, restartedCache);
            var restored = (await restartedRepository.GetOpenPositionsAsync()).Single();
            var coordinator = new LivePositionExecutionCoordinator(
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

            firstResult.Status.Should().Be(LivePositionExecutionReconciliationStatus.Completed);
            duplicateResult.Status.Should().Be(LivePositionExecutionReconciliationStatus.NotPending);
        }

        await using var verifyDb = new AppDbContext(options);
        var stored = await verifyDb.Positions.AsNoTracking().SingleAsync();
        stored.Quantity.Should().Be(6);
        stored.PartialProfitTaken.Should().BeTrue();
        stored.ExecutionRequestedAt.Should().BeNull();
        (await verifyDb.TradeRecords.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ManualPartialFillDoesNotMarkStrategyProfitOrMoveStop()
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
            StopLossPrice = 45m,
        };
        await repository.SavePositionAsync(position);
        var requestedAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var claim = new PositionExecutionClaim(
            position.Id, requestedAt, "수동 일부 매도", 10, 4,
            PositionExecutionKind.PartialProfit, MarksPartialProfit: false);
        (await repository.TryClaimPositionExecutionAsync(claim)).Should().BeTrue();

        var applied = await repository.TryApplyPositionExecutionFillAsync(
            new PositionExecutionFill(
                position.Id, requestedAt, 10, 4, 52m, requestedAt, "manual-1",
                PositionExecutionKind.PartialProfit),
            new TradeRecord
            {
                Symbol = "TQQQ",
                EntryPrice = 50m,
                ExitPrice = 52m,
                Quantity = 4,
                ExitReason = "수동 일부 매도",
            });

        applied.Should().BeTrue();
        var stored = await db.Positions.AsNoTracking().SingleAsync();
        stored.Quantity.Should().Be(6);
        stored.PartialProfitTaken.Should().BeFalse();
        stored.StopLossPrice.Should().Be(45m);
        stored.BreakevenApplied.Should().BeFalse();
    }

    [Fact]
    public async Task ScalingFillsPersistWeightedAverageReducedQuantityAndRuleCounts()
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
        };
        await repository.SavePositionAsync(position);
        var scaleInAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var scaleInClaim = new PositionExecutionClaim(
            position.Id, scaleInAt, "추가 매수", 10, 4,
            PositionExecutionKind.ScaleIn, ScalingRuleIndex: 2);

        (await repository.TryClaimPositionExecutionAsync(scaleInClaim)).Should().BeTrue();
        var scaleInFill = new PositionExecutionFill(
            position.Id, scaleInAt, 10, 4, 55m, scaleInAt.AddMinutes(1), "buy-1",
            PositionExecutionKind.ScaleIn, ScalingRuleIndex: 2);
        (await repository.TryApplyPositionExecutionFillAsync(scaleInFill, null)).Should().BeTrue();
        (await repository.TryApplyPositionExecutionFillAsync(scaleInFill, null)).Should().BeFalse();

        var scaleOutAt = scaleInAt.AddMinutes(2);
        var scaleOutClaim = new PositionExecutionClaim(
            position.Id, scaleOutAt, "분할 매도", 14, 3,
            PositionExecutionKind.ScaleOut, ScalingRuleIndex: 1);
        (await repository.TryClaimPositionExecutionAsync(scaleOutClaim)).Should().BeTrue();
        var scaleOutFill = new PositionExecutionFill(
            position.Id, scaleOutAt, 14, 3, 60m, scaleOutAt.AddMinutes(1), "sell-1",
            PositionExecutionKind.ScaleOut, ScalingRuleIndex: 1);
        var trade = new TradeRecord
        {
            Symbol = "TQQQ",
            EntryPrice = 51.428571m,
            ExitPrice = 60m,
            Quantity = 3,
            ExitReason = "분할 매도",
        };
        (await repository.TryApplyPositionExecutionFillAsync(scaleOutFill, trade)).Should().BeTrue();

        var stored = await db.Positions
            .AsNoTracking()
            .Include(item => item.ScalingExecutions)
            .SingleAsync();
        stored.Quantity.Should().Be(11);
        stored.EntryPrice.Should().BeApproximately(51.428571m, 0.000001m);
        stored.ScalingExecutionCounts.Should().BeEquivalentTo(
            new Dictionary<int, int> { [2] = 1, [1] = 1 });
        (await db.TradeRecords.AsNoTracking().SingleAsync()).Quantity.Should().Be(3);
    }

    [Fact]
    public async Task ScalingClaimWithoutRuleIndexFailsClosed()
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

        var claim = new PositionExecutionClaim(
            position.Id, DateTime.UtcNow, "추가 매수", 10, 2,
            PositionExecutionKind.ScaleIn);

        (await repository.TryClaimPositionExecutionAsync(claim)).Should().BeFalse();
        (await db.Positions.AsNoTracking().SingleAsync()).ExecutionRequestedAt.Should().BeNull();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
