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
        var executionStore = new LivePositionExecutionStore(
            new TestDbContextFactory(options), cache);
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
        (await executionStore.TryClaimAsync(claim)).Should().BeTrue();
        var filledAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);
        var fill = new PositionExecutionFill(
            position.Id, requestedAt, 2, 2, 55m, filledAt, "exit-1");
        var trade = ExecutionTrade("TQQQ", 50m, 55m, 2, "목표 도달");

        (await executionStore.CommitFillAsync(fill, trade)).Should().BeTrue();
        (await executionStore.CommitFillAsync(fill, trade)).Should().BeFalse();

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
        var executionStore = new LivePositionExecutionStore(
            new TestDbContextFactory(options), cache);
        var position = new Position { Symbol = "TQQQ", Quantity = 10, InitialQuantity = 10 };
        await repository.SavePositionAsync(position);
        var first = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc);

        var firstClaim = new PositionExecutionClaim(
            position.Id, first, "1차 이익실현", 10, 4,
            PositionExecutionKind.PartialProfit, MarksPartialProfit: true);
        (await executionStore.TryClaimAsync(
            firstClaim with { ExpectedPositionQuantity = 9 })).Should().BeFalse();
        (await executionStore.TryClaimAsync(firstClaim)).Should().BeTrue();
        (await executionStore.TryClaimAsync(
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
        var executionStore = new LivePositionExecutionStore(
            new TestDbContextFactory(options), cache);
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
        (await executionStore.TryClaimAsync(claim)).Should().BeTrue();
        var fill = new PositionExecutionFill(
            position.Id, requestedAt, 10, 4, 55m, requestedAt.AddMinutes(1), "exit-2",
            PositionExecutionKind.PartialProfit, MarksPartialProfit: true);
        var trade = ExecutionTrade("TQQQ", 50m, 55m, 4, "1차 이익실현");

        (await executionStore.CommitFillAsync(fill, trade)).Should().BeTrue();
        (await executionStore.CommitFillAsync(fill, trade)).Should().BeFalse();

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
            var firstExecutionStore = new LivePositionExecutionStore(
                new TestDbContextFactory(options), firstCache);
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
            (await firstExecutionStore.TryClaimAsync(claim)).Should().BeTrue();
            (await firstExecutionStore.SetOrderEvidenceAsync(
                position.Id, requestedAt, "exit-restart")).Should().BeTrue();
        }

        await using (var restartedDb = new AppDbContext(options))
        {
            using var restartedCache = new MemoryCache(new MemoryCacheOptions());
            var restartedRepository = new TradeRepository(restartedDb, restartedCache);
            var restartedExecutionStore = new LivePositionExecutionStore(
                new TestDbContextFactory(options), restartedCache);
            var restored = (await restartedRepository.GetOpenPositionsAsync()).Single();
            var coordinator = new LivePositionExecutionCoordinator(
                restartedExecutionStore,
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
        var executionStore = new LivePositionExecutionStore(
            new TestDbContextFactory(options), cache);
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
        (await executionStore.TryClaimAsync(claim)).Should().BeTrue();

        var applied = await executionStore.CommitFillAsync(
            new PositionExecutionFill(
                position.Id, requestedAt, 10, 4, 52m, requestedAt, "manual-1",
                PositionExecutionKind.PartialProfit),
            ExecutionTrade("TQQQ", 50m, 52m, 4, "수동 일부 매도"));

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
        var executionStore = new LivePositionExecutionStore(
            new TestDbContextFactory(options), cache);
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

        (await executionStore.TryClaimAsync(scaleInClaim)).Should().BeTrue();
        var scaleInFill = new PositionExecutionFill(
            position.Id, scaleInAt, 10, 4, 55m, scaleInAt.AddMinutes(1), "buy-1",
            PositionExecutionKind.ScaleIn, ScalingRuleIndex: 2);
        (await executionStore.CommitFillAsync(scaleInFill, null)).Should().BeTrue();
        (await executionStore.CommitFillAsync(scaleInFill, null)).Should().BeFalse();

        var scaleOutAt = scaleInAt.AddMinutes(2);
        var scaleOutClaim = new PositionExecutionClaim(
            position.Id, scaleOutAt, "분할 매도", 14, 3,
            PositionExecutionKind.ScaleOut, ScalingRuleIndex: 1);
        (await executionStore.TryClaimAsync(scaleOutClaim)).Should().BeTrue();
        var scaleOutFill = new PositionExecutionFill(
            position.Id, scaleOutAt, 14, 3, 60m, scaleOutAt.AddMinutes(1), "sell-1",
            PositionExecutionKind.ScaleOut, ScalingRuleIndex: 1);
        var trade = ExecutionTrade("TQQQ", 51.428571m, 60m, 3, "분할 매도");
        (await executionStore.CommitFillAsync(scaleOutFill, trade)).Should().BeTrue();

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
        var executionStore = new LivePositionExecutionStore(
            new TestDbContextFactory(options), cache);
        var position = new Position { Symbol = "TQQQ", Quantity = 10, InitialQuantity = 10 };
        await repository.SavePositionAsync(position);

        var claim = new PositionExecutionClaim(
            position.Id, DateTime.UtcNow, "추가 매수", 10, 2,
            PositionExecutionKind.ScaleIn);

        (await executionStore.TryClaimAsync(claim)).Should().BeFalse();
        (await db.Positions.AsNoTracking().SingleAsync()).ExecutionRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task AddSignalsBatchAsync_IsIdempotentPerStrategyAndSignalBar()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var barAt = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc);

        var firstDuplicate = Signal("AAPL", PatternType.Breakout, null, barAt, barAt.AddHours(1));
        var secondDuplicate = Signal("AAPL", PatternType.Breakout, null, barAt, barAt.AddHours(2));
        await repository.AddSignalsBatchAsync([
            firstDuplicate,
            secondDuplicate,
            Signal("AAPL", PatternType.Custom, "alpha", barAt, barAt.AddHours(1)),
            Signal("AAPL", PatternType.Custom, "beta", barAt, barAt.AddHours(1))
        ]);
        var restoredDuplicate = Signal(
            "aapl", PatternType.Breakout, null, barAt, barAt.AddHours(3));
        await repository.AddSignalsBatchAsync([
            restoredDuplicate,
            Signal("AAPL", PatternType.Custom, "ALPHA", barAt, barAt.AddHours(3))
        ]);

        var stored = await db.PatternSignals.AsNoTracking().OrderBy(signal => signal.Id).ToListAsync();
        stored.Should().HaveCount(3);
        stored.Select(signal => signal.CustomPatternName).Should().BeEquivalentTo([null, "alpha", "beta"]);
        firstDuplicate.Id.Should().BeGreaterThan(0);
        secondDuplicate.Id.Should().Be(firstDuplicate.Id);
        restoredDuplicate.Id.Should().Be(firstDuplicate.Id);
    }

    [Fact]
    public async Task AddSignalAsync_RejectsMissingSignalBarIdentity()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"signals-{Guid.NewGuid()}")
            .Options;
        await using var db = new AppDbContext(options);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var signal = Signal("AAPL", PatternType.Breakout, null, DateTime.UtcNow, DateTime.UtcNow);
        signal.SignalBarAt = null;

        var act = () => repository.AddSignalAsync(signal);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*signal bar timestamp*");
    }

    [Fact]
    public async Task AddRecommendationAsync_ReusesPersistedRecommendationForSameSignal()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var repository = new TradeRepository(db, cache);
        var first = Recommendation(sourceSignalId: 81, entryPrice: 100m);
        await repository.AddRecommendationAsync(first);
        first.EntryRequestedAt = new DateTime(2026, 8, 18, 15, 0, 0);
        first.EntryAccountId = 7;
        first.EntryOrderId = "entry-81";
        await repository.UpdateRecommendationAsync(first);
        var duplicate = Recommendation(sourceSignalId: 81, entryPrice: 999m);

        await repository.AddRecommendationAsync(duplicate);

        (await db.TradeRecommendations.CountAsync()).Should().Be(1);
        duplicate.Id.Should().Be(first.Id);
        duplicate.EntryPrice.Should().Be(100m);
        duplicate.EntryRequestedAt.Should().Be(first.EntryRequestedAt);
        duplicate.EntryOrderId.Should().Be("entry-81");
    }

    private static PatternSignal Signal(
        string symbol,
        PatternType patternType,
        string? customPatternName,
        DateTime signalBarAt,
        DateTime detectedAt) => new()
        {
            Symbol = symbol,
            PatternType = patternType,
            CustomPatternName = customPatternName,
            SignalBarAt = signalBarAt,
            DetectedAt = detectedAt,
            EntryPrice = 100m,
            StopLossPrice = 95m,
            TargetPrice = 110m,
            Confidence = 0.8m,
            IsActive = true
        };

    private static TradeRecommendation Recommendation(long sourceSignalId, decimal entryPrice) => new()
    {
        SourceSignalId = sourceSignalId,
        Symbol = "TQQQ",
        PatternType = PatternType.GapUpPullback,
        GeneratedAt = new DateTime(2026, 8, 18, 14, 0, 0),
        EntryPrice = entryPrice,
        StopLossPrice = 95m,
        TargetPrice = 110m,
        ShareQuantity = 10,
    };

    private static PositionExecutionTrade ExecutionTrade(
        string symbol,
        decimal entryPrice,
        decimal exitPrice,
        int quantity,
        string reason) => new(
            symbol,
            PatternType.GapUpPullback,
            null,
            entryPrice,
            exitPrice,
            quantity,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch,
            (exitPrice - entryPrice) * quantity,
            entryPrice > 0 ? exitPrice / entryPrice - 1 : 0,
            reason);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
