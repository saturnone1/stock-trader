using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Data;
using StockTrader.Data.Migrations;

namespace StockTrader.Tests;

public class DatabaseSchemaMigratorTests
{
    [Fact]
    public async Task EmptyDatabaseIsCreatedOnlyByEfMigrations()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var migrator = CreateMigrator(db);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        (await TableExistsAsync(connection, "OhlcvBars")).Should().BeTrue();
        (await TableExistsAsync(connection, "CustomPatterns")).Should().BeTrue();
        (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeTrue();
        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM __EFMigrationsHistory")).Should().Be(1);
        (await TableExistsAsync(connection, "__StockTraderMigrations")).Should().BeFalse();
    }

    [Fact]
    public async Task EfManagedDatabasePreservesRowsAcrossIdempotentStartup()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var migrator = CreateMigrator(db);
        await migrator.MigrateAsync();
        db.Positions.Add(new StockTrader.Models.Position
        {
            Symbol = "TQQQ",
            EntryPrice = 100m,
            CurrentPrice = 101m,
            StopLossPrice = 95m,
            TargetPrice = 110m,
            Quantity = 3
        });
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();

        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM Positions WHERE Symbol = 'TQQQ' AND Quantity = 3")).Should().Be(1);
        (await ScalarAsync<string>(connection,
            "SELECT MigrationId FROM __EFMigrationsHistory LIMIT 1"))
            .Should().EndWith(DatabaseSchemaMigrator.InitialMigrationSuffix);
    }

    [Fact]
    public async Task DatabaseWithoutEfHistoryFailsClosedWithoutChangingRows()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE Positions (Id INTEGER PRIMARY KEY AUTOINCREMENT, Symbol TEXT NOT NULL DEFAULT '');
            INSERT INTO Positions (Symbol) VALUES ('TQQQ');
            """);
        await using var db = CreateContext(connection);

        var action = () => CreateMigrator(db).MigrateAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EF 마이그레이션 이력이 없는 기존 데이터베이스*");
        (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeFalse();
        (await TableExistsAsync(connection, "__StockTraderMigrations")).Should().BeFalse();
        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM Positions WHERE Symbol = 'TQQQ'")).Should().Be(1);
    }

    [Fact]
    public async Task DurablePositionExecutionStateRoundTripsThroughEfSchema()
    {
        await using var connection = await OpenConnectionAsync();
        await using (var writeDb = CreateContext(connection))
        {
            await CreateMigrator(writeDb).MigrateAsync();
            writeDb.Positions.Add(new StockTrader.Models.Position
            {
                Symbol = "TQQQ",
                InitialRiskDistance = 4.25m,
                BreakevenApplied = true,
                TrailingStopActivated = true,
                ExitRequestedAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc),
                ExitRequestReason = "손절",
                ExitOrderId = "order-123",
            });
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext(connection);
        var restored = await readDb.Positions.AsNoTracking().SingleAsync();

        restored.InitialRiskDistance.Should().Be(4.25m);
        restored.BreakevenApplied.Should().BeTrue();
        restored.TrailingStopActivated.Should().BeTrue();
        restored.ExitRequestReason.Should().Be("손절");
        restored.ExitOrderId.Should().Be("order-123");
    }

    private static DatabaseSchemaMigrator CreateMigrator(AppDbContext db) =>
        new(db, NullLogger<DatabaseSchemaMigrator>.Instance);

    private static AppDbContext CreateContext(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table) =>
        await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'") > 0;
}
