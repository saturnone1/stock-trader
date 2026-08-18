using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockTrader.Api.Contracts;
using StockTrader.Application.SymbolProfiles;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Domain.MarketData;
using StockTrader.Models;

namespace StockTrader.Tests;

public class SymbolProfileManagementServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpsertAsync_NormalizesIdentityAndUsesCentralDefaults()
    {
        var store = new Mock<ISymbolProfileStore>();
        store.Setup(value => value.GetBySymbolAndNameAsync(
                "TQQQ", "기본", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedSymbolProfile?)null);
        store.Setup(value => value.SaveAsync(
                It.IsAny<ManagedSymbolProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedSymbolProfile value, CancellationToken _) => value with { Id = 42 });
        var service = CreateService(store.Object);

        var outcome = await service.UpsertAsync(new SymbolProfileUpsertCommand
        {
            Symbol = " tqqq ",
            EnabledPatterns = [PatternType.Breakout, PatternType.Breakout]
        });

        outcome.Succeeded.Should().BeTrue();
        outcome.Created.Should().BeTrue();
        outcome.Profile.Should().BeEquivalentTo(new
        {
            Id = 42L,
            Symbol = "TQQQ",
            Name = "기본",
            RiskPerTradePercent = 0.01m,
            MaxTotalPositions = 7,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        });
        outcome.Profile!.EnabledPatterns.Should().Equal(PatternType.Breakout);
    }

    [Fact]
    public async Task UpsertAsync_PreservesOmittedValuesWhenUpdating()
    {
        var current = Profile() with
        {
            Id = 7,
            EnabledPatterns = [PatternType.Breakout],
            ParameterOverridesJson = "{\"lookback\":20}",
            RiskPerTradePercent = 0.02m,
            MaxTotalPositions = 3,
            BacktestReturnPct = 12.5m,
            CreatedAt = Now.AddDays(-10).UtcDateTime
        };
        var store = new Mock<ISymbolProfileStore>();
        store.Setup(value => value.GetBySymbolAndNameAsync(
                "TQQQ", "기본", It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        store.Setup(value => value.SaveAsync(
                It.IsAny<ManagedSymbolProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManagedSymbolProfile value, CancellationToken _) => value);
        var service = CreateService(store.Object);

        var outcome = await service.UpsertAsync(new SymbolProfileUpsertCommand
        {
            Symbol = "TQQQ",
            Name = "기본",
            WeightStrategyJson = "{\"mode\":\"equal\"}"
        });

        outcome.Created.Should().BeFalse();
        outcome.Profile.Should().NotBeNull();
        outcome.Profile!.EnabledPatterns.Should().Equal(PatternType.Breakout);
        outcome.Profile.ParameterOverridesJson.Should().Be("{\"lookback\":20}");
        outcome.Profile.RiskPerTradePercent.Should().Be(0.02m);
        outcome.Profile.MaxTotalPositions.Should().Be(3);
        outcome.Profile.BacktestReturnPct.Should().Be(12.5m);
        outcome.Profile.CreatedAt.Should().Be(current.CreatedAt);
        outcome.Profile.UpdatedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task UpsertAsync_RejectsUnsupportedOrMalformedConfigurationBeforePersistence()
    {
        var store = new Mock<ISymbolProfileStore>(MockBehavior.Strict);
        var service = CreateService(store.Object);

        var outcome = await service.UpsertAsync(new SymbolProfileUpsertCommand
        {
            Symbol = "bad symbol!",
            Name = new string('x', 81),
            EnabledPatterns = [PatternType.Custom],
            ParameterOverridesJson = "[1,2]",
            WeightStrategyJson = "{broken",
            RiskPerTradePercent = 2m,
            MaxTotalPositions = 0,
            BacktestTrades = -1,
            BacktestFrom = new DateTime(2026, 8, 2),
            BacktestTo = new DateTime(2026, 8, 1)
        });

        outcome.Succeeded.Should().BeFalse();
        outcome.Errors.Should().HaveCount(9);
    }

    [Fact]
    public async Task SetActiveAsync_LeavesOnlyOneActiveProfilePerSymbol()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.SymbolProfiles.AddRange(
            Entity("TQQQ", "공격형", true),
            Entity("TQQQ", "보수형", false),
            Entity("AAPL", "기본", true));
        await db.SaveChangesAsync();
        var targetId = await db.SymbolProfiles
            .Where(value => value.Symbol == "TQQQ" && value.Name == "보수형")
            .Select(value => value.Id)
            .SingleAsync();
        var store = new SymbolProfileStore(db);

        var activated = await store.SetActiveAsync(
            targetId, true, Now.UtcDateTime);

        activated.Should().NotBeNull();
        activated!.IsActive.Should().BeTrue();
        var tqqq = await db.SymbolProfiles.AsNoTracking()
            .Where(value => value.Symbol == "TQQQ")
            .OrderBy(value => value.Name)
            .ToListAsync();
        tqqq.Should().ContainSingle(value => value.IsActive && value.Name == "보수형");
        (await db.SymbolProfiles.AsNoTracking()
            .SingleAsync(value => value.Symbol == "AAPL")).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task StoreReturnsDetachedPatternCollections()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.SymbolProfiles.Add(Entity("TQQQ", "기본", true));
        await db.SaveChangesAsync();
        var store = new SymbolProfileStore(db);

        var first = (await store.ListAsync("TQQQ")).Single();
        var mutableCopy = first.EnabledPatterns.ToList();
        mutableCopy.Add(PatternType.GapUpPullback);
        var second = (await store.ListAsync("TQQQ")).Single();

        second.EnabledPatterns.Should().Equal(PatternType.Breakout);
    }

    [Theory]
    [InlineData(" tqqq ", "TQQQ", true)]
    [InlineData("brk.b", "BRK.B", true)]
    [InlineData("bad symbol", "BAD SYMBOL", false)]
    [InlineData("", "", false)]
    public void MarketSymbolPolicy_OwnsNormalizationAndValidation(
        string input,
        string normalized,
        bool isValid)
    {
        MarketSymbolPolicy.Normalize(input).Should().Be(normalized);
        MarketSymbolPolicy.IsValid(input).Should().Be(isValid);
    }

    [Fact]
    public void ResponseContractPreservesTheLegacyDateWireFormat()
    {
        var response = SymbolProfileResponse.Create(Profile() with
        {
            BacktestFrom = new DateTime(2026, 1, 2),
            BacktestTo = new DateTime(2026, 3, 4),
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc)
        });

        response.BacktestFrom.Should().Be("2026-01-02");
        response.BacktestTo.Should().Be("2026-03-04");
        response.CreatedAt.Should().Be("2026-01-02T03:04:05.0000000Z");
        response.UpdatedAt.Should().Be("2026-03-04T05:06:07.0000000Z");
    }

    private static SymbolProfileManagementService CreateService(ISymbolProfileStore store) =>
        new(store, new FixedTimeProvider(Now));

    private static ManagedSymbolProfile Profile() => new()
    {
        Symbol = "TQQQ",
        Name = "기본",
        RiskPerTradePercent = 0.01m,
        MaxTotalPositions = 7
    };

    private static SymbolProfile Entity(string symbol, string name, bool isActive) => new()
    {
        Symbol = symbol,
        Name = name,
        IsActive = isActive,
        EnabledPatterns = [PatternType.Breakout],
        RiskPerTradePercent = 0.01m,
        MaxTotalPositions = 7,
        CreatedAt = Now.UtcDateTime,
        UpdatedAt = Now.UtcDateTime
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
