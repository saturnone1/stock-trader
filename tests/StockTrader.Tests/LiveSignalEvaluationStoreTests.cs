using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Repositories;
using StockTrader.Models;

namespace StockTrader.Tests;

public sealed class LiveSignalEvaluationStoreTests
{
    [Fact]
    public async Task LoadAsyncProjectsOnlyTheStateNeededForSignalEvaluation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();

        var sessionStart = new DateTime(2025, 1, 6, 5, 0, 0, DateTimeKind.Utc);
        db.TradeRecords.AddRange(
            CompletedTrade("Momentum", new DateTime(2025, 1, 3), -20m, -0.02m),
            CompletedTrade("MOMENTUM", new DateTime(2025, 1, 2), 10m, 0.01m),
            CompletedTrade("Other", new DateTime(2025, 1, 4), 30m, 0.03m));
        db.Positions.AddRange(
            new Position { Symbol = "OPEN", ClosedAt = null },
            new Position { Symbol = "CLOSED", ClosedAt = sessionStart });
        db.TradeRecommendations.AddRange(
            ExecutedRecommendation("Momentum", sessionStart),
            ExecutedRecommendation("MOMENTUM", sessionStart.AddHours(1)),
            ExecutedRecommendation("Momentum", sessionStart.AddTicks(-1)),
            ExecutedRecommendation("Other", sessionStart.AddHours(1)),
            ExecutedRecommendation("Momentum", sessionStart.AddHours(2), wasExecuted: false));
        db.Tickers.AddRange(
            new Ticker { Symbol = "AAPL", Sector = "Technology" },
            new Ticker { Symbol = "MSFT", Sector = "Software" });
        await db.SaveChangesAsync();

        var snapshot = await new LiveSignalEvaluationStore(db).LoadAsync(
            ["momentum"],
            ["aapl"],
            sessionStart);

        snapshot.CompletedTradesFor("MOMENTUM")
            .Select(trade => trade.RealizedPnl)
            .Should().Equal(10m, -20m);
        snapshot.CompletedTradesFor("Other").Should().BeEmpty();
        snapshot.OpenPositionCount.Should().Be(1);
        snapshot.ExecutedEntriesFor("momentum").Should().Be(2);
        snapshot.ExecutedEntriesFor("Other").Should().Be(0);
        snapshot.SectorFor("aapl").Should().Be("Technology");
        snapshot.SectorFor("MSFT").Should().BeNull();
    }

    [Fact]
    public async Task LoadAsyncWithoutCustomStrategiesSkipsPortfolioAndHistoryButStillLoadsSectors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateContext(connection);
        await db.Database.EnsureCreatedAsync();
        db.Positions.Add(new Position { Symbol = "OPEN", ClosedAt = null });
        db.TradeRecords.Add(CompletedTrade("Momentum", new DateTime(2025, 1, 2), 10m, 0.01m));
        db.Tickers.Add(new Ticker { Symbol = "TQQQ", Sector = "Leveraged ETF" });
        await db.SaveChangesAsync();

        var snapshot = await new LiveSignalEvaluationStore(db).LoadAsync(
            [],
            ["TQQQ"],
            new DateTime(2025, 1, 6, 5, 0, 0, DateTimeKind.Utc));

        snapshot.OpenPositionCount.Should().Be(0);
        snapshot.CompletedTradesByStrategy.Should().BeEmpty();
        snapshot.ExecutedEntriesByStrategy.Should().BeEmpty();
        snapshot.SectorFor("tqqq").Should().Be("Leveraged ETF");
    }

    private static AppDbContext CreateContext(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options);

    private static TradeRecord CompletedTrade(
        string strategyName,
        DateTime exitTime,
        decimal pnl,
        decimal returnFraction) => new()
        {
            Symbol = "AAPL",
            CustomPatternName = strategyName,
            ExitTime = exitTime,
            PnL = pnl,
            PnLPercent = returnFraction
        };

    private static TradeRecommendation ExecutedRecommendation(
        string strategyName,
        DateTime generatedAt,
        bool wasExecuted = true) => new()
        {
            Symbol = "AAPL",
            CustomPatternName = strategyName,
            GeneratedAt = generatedAt,
            WasExecuted = wasExecuted
        };
}
