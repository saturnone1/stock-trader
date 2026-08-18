using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Tests;

public sealed class LiveEntryExecutionStoreTests
{
    [Fact]
    public async Task ClaimedFillCommitsRecommendationAndPositionTogether()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new LiveEntryExecutionStore(database.Factory, cache);
        var recommendation = Recommendation();
        await using (var seed = database.Factory.CreateDbContext())
        {
            seed.TradeRecommendations.Add(recommendation);
            await seed.SaveChangesAsync();
        }
        var position = Position(accountId: 27);
        var requestedAt = new DateTime(2026, 8, 18, 15, 1, 0);

        (await store.TryClaimAsync(recommendation, 27, requestedAt)).Should().BeTrue();
        (await store.SetExecutionNoteAsync(
            recommendation, requestedAt, "접수 확인 필요")).Should().BeTrue();
        (await store.SetOrderEvidenceAsync(
            recommendation, requestedAt, "entry-1")).Should().BeTrue();
        (await store.CommitFilledEntryAsync(
            recommendation, requestedAt, position)).Should().BeTrue();

        await using var verify = database.Factory.CreateDbContext();
        (await verify.TradeRecommendations.SingleAsync()).WasExecuted.Should().BeTrue();
        var storedPosition = await verify.Positions.SingleAsync();
        storedPosition.AccountId.Should().Be(27);
        storedPosition.Symbol.Should().Be("TQQQ");
        position.Id.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task MissingRecommendationRollsBackWithoutCreatingPosition()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new LiveEntryExecutionStore(database.Factory, cache);
        var recommendation = Recommendation();
        recommendation.Id = 999;

        (await store.TryClaimAsync(
            recommendation, 27, DateTime.UtcNow)).Should().BeFalse();
        (await store.CommitFilledEntryAsync(
            recommendation,
            DateTime.UtcNow,
            Position(accountId: 27))).Should().BeFalse();
        await using var verify = database.Factory.CreateDbContext();
        (await verify.Positions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AlreadyExecutedRecommendationCannotCreateAnotherPosition()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new LiveEntryExecutionStore(database.Factory, cache);
        var recommendation = Recommendation();
        recommendation.WasExecuted = true;
        await using (var seed = database.Factory.CreateDbContext())
        {
            seed.TradeRecommendations.Add(recommendation);
            await seed.SaveChangesAsync();
        }

        (await store.TryClaimAsync(
            recommendation, 27, DateTime.UtcNow)).Should().BeFalse();
        (await store.CommitFilledEntryAsync(
            recommendation,
            DateTime.UtcNow,
            Position(accountId: 27))).Should().BeFalse();
        await using var verify = database.Factory.CreateDbContext();
        (await verify.Positions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ClaimAllowsOnlyOneWorkerAndSurvivesReload()
    {
        await using var database = await TestDatabase.CreateAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new LiveEntryExecutionStore(database.Factory, cache);
        var recommendation = Recommendation();
        await using (var seed = database.Factory.CreateDbContext())
        {
            seed.TradeRecommendations.Add(recommendation);
            await seed.SaveChangesAsync();
        }
        var requestedAt = new DateTime(2026, 8, 18, 15, 1, 0);

        (await store.TryClaimAsync(recommendation, 27, requestedAt)).Should().BeTrue();
        (await store.TryClaimAsync(recommendation, 27, requestedAt.AddSeconds(1)))
            .Should().BeFalse();
        (await store.SetExecutionNoteAsync(
            recommendation, requestedAt, "접수 확인 필요")).Should().BeTrue();

        var restored = await store.LoadAsync(recommendation.Id);
        restored!.EntryRequestedAt.Should().Be(requestedAt);
        restored.EntryAccountId.Should().Be(27);
        restored.EntryExecutionNote.Should().Be("접수 확인 필요");
        (await store.LoadPendingAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ManualSignalLookupIsDetachedAndPurposeSpecific()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using (var seed = database.Factory.CreateDbContext())
        {
            seed.PatternSignals.Add(new PatternSignal
            {
                Id = 44,
                Symbol = "TQQQ",
                PatternType = PatternType.GapUpPullback,
                DetectedAt = new DateTime(2026, 8, 18, 14, 0, 0),
                EntryPrice = 100m,
                StopLossPrice = 95m,
                TargetPrice = 110m
            });
            await seed.SaveChangesAsync();
        }
        var store = new ManualOrderSignalStore(database.Factory);

        var signal = await store.LoadAsync(44);

        signal.Should().NotBeNull();
        signal!.Symbol.Should().Be("TQQQ");
        (await store.LoadAsync(45)).Should().BeNull();
    }

    private static TradeRecommendation Recommendation() => new()
    {
        Symbol = "TQQQ",
        PatternType = PatternType.GapUpPullback,
        GeneratedAt = new DateTime(2026, 8, 18, 15, 0, 0),
        EntryPrice = 100m,
        StopLossPrice = 95m,
        TargetPrice = 110m,
        PositionSize = 1_000m,
        ShareQuantity = 10,
        Mode = OrderMode.AutoOrder
    };

    private static Position Position(int accountId) => new()
    {
        AccountId = accountId,
        Symbol = "TQQQ",
        Quantity = 10,
        InitialQuantity = 10,
        EntryPrice = 100m,
        CurrentPrice = 100m,
        StopLossPrice = 95m,
        TargetPrice = 110m,
        PatternType = PatternType.GapUpPullback,
        OpenedAt = new DateTime(2026, 8, 18, 15, 0, 0)
    };

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public TestFactory Factory { get; }

        private TestDatabase(SqliteConnection connection, TestFactory factory)
        {
            _connection = connection;
            Factory = factory;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var factory = new TestFactory(options);
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, factory);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class TestFactory(DbContextOptions<AppDbContext> options)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
