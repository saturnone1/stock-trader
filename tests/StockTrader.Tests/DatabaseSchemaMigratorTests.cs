using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
            "SELECT COUNT(*) FROM __EFMigrationsHistory"))
            .Should().Be(db.Database.GetMigrations().LongCount());
        (await TableExistsAsync(connection, "__StockTraderMigrations")).Should().BeFalse();
    }

    [Fact]
    public async Task ExistingStrategyDocumentsReceiveVersionAndNormalizedNameThroughEfMigrations()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);

        var initialMigration = db.Database.GetMigrations()
            .Single(value => value.EndsWith(DatabaseSchemaMigrator.InitialMigrationSuffix, StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(initialMigration);
        await ExecuteAsync(connection, """
            INSERT INTO CustomPatterns (
                Name, EntryRulesJson, EntryLogic, RequireBullRegime,
                AtrStopMultiplier, AtrTargetMultiplier, MaxHoldingBars, TrailingAtr, PartialProfitR,
                UseWeightTiers, WeightTiersJson, DefaultAllocationPercent,
                ExitRulesJson, ExitRulesLogic, ExitGroupsJson, ExitGroupsLogic,
                ScalingRulesJson, TimeFilterJson, CircuitBreakerJson, ReentryJson,
                PortfolioRulesJson, EntryGroupsJson, EntryGroupsLogic, DynamicExitJson,
                EntryMode, TimeFrame, SizingMode, IsActive, EnableLiveTrading, CreatedAt, UpdatedAt)
            VALUES (
                '버전 전략', '[]', 'AND', 0,
                2, 3, 10, 0, 0,
                0, '[]', 100,
                '[]', 'OR', '[]', 'OR',
                '[]', '{}', '{}', '{}',
                '{}', '[]', 'AND', '{}',
                'CurrentClose', 0, 'FixedRisk', 1, 0,
                '2026-08-18T00:00:00Z', '2026-08-18T00:00:00Z');
            """);

        await CreateMigrator(db).MigrateAsync();

        var storedVersion = await ScalarAsync<long>(connection,
            "SELECT DocumentVersion FROM CustomPatterns WHERE Name = '버전 전략'");
        storedVersion.Should().Be(1);
        (await ScalarAsync<string>(connection,
            "SELECT NormalizedName FROM CustomPatterns WHERE Name = '버전 전략'"))
            .Should().Be("버전 전략");
        (await ScalarAsync<long>(connection, """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'IX_CustomPatterns_NormalizedName'
            """)).Should().Be(1);
    }

    [Fact]
    public async Task CaseOnlyLegacyDuplicatesFailClosedWithoutPartiallyApplyingNameMigration()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var versionMigration = db.Database.GetMigrations().Single(value =>
            value.EndsWith("_AddStrategyDocumentVersion", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(versionMigration);
        await InsertVersionedStrategyAsync(connection, "Momentum");
        await InsertVersionedStrategyAsync(connection, "momentum");

        var action = () => CreateMigrator(db).MigrateAsync();

        await action.Should().ThrowAsync<SqliteException>()
            .WithMessage("*UNIQUE constraint failed: CustomPatterns.NormalizedName*");
        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM CustomPatterns")).Should().Be(2);
        (await ScalarAsync<long>(connection, """
            SELECT COUNT(*) FROM pragma_table_info('CustomPatterns')
            WHERE name = 'NormalizedName'
            """)).Should().Be(0);
        (await ScalarAsync<string>(connection, """
            SELECT MigrationId FROM __EFMigrationsHistory
            ORDER BY MigrationId DESC LIMIT 1
            """)).Should().EndWith("_AddStrategyDocumentVersion");
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
                Quantity = 10,
                InitialQuantity = 12,
                InitialRiskDistance = 4.25m,
                BreakevenApplied = true,
                TrailingStopActivated = true,
                PartialProfitTaken = true,
                ExitRequestedAt = new DateTime(2026, 8, 18, 14, 0, 0, DateTimeKind.Utc),
                ExitRequestReason = "손절",
                ExitRequestQuantity = 4,
                ExitRequestMarksPartialProfit = true,
                ExitOrderId = "order-123",
            });
            await writeDb.SaveChangesAsync();
        }

        await using var readDb = CreateContext(connection);
        var restored = await readDb.Positions.AsNoTracking().SingleAsync();

        restored.InitialRiskDistance.Should().Be(4.25m);
        restored.InitialQuantity.Should().Be(12);
        restored.BreakevenApplied.Should().BeTrue();
        restored.TrailingStopActivated.Should().BeTrue();
        restored.PartialProfitTaken.Should().BeTrue();
        restored.ExitRequestReason.Should().Be("손절");
        restored.ExitRequestQuantity.Should().Be(4);
        restored.ExitRequestMarksPartialProfit.Should().BeTrue();
        restored.ExitOrderId.Should().Be("order-123");
    }

    [Fact]
    public async Task PositionExitQuantityMigrationBackfillsLegacyOpenAndPendingState()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var previousMigration = db.Database.GetMigrations().Single(value =>
            value.EndsWith("_AddNormalizedCustomPatternName", StringComparison.Ordinal));
        await db.GetService<IMigrator>().MigrateAsync(previousMigration);
        await ExecuteAsync(connection, """
            INSERT INTO Positions (
                AccountId, Symbol, Sector, Quantity,
                EntryPrice, CurrentPrice, StopLossPrice, TargetPrice,
                PatternType, OpenedAt, HighSinceEntry, EntryAtr, InitialRiskDistance,
                BreakevenApplied, TrailingStopActivated,
                ExitRequestedAt, ExitRequestReason, ExitOrderId)
            VALUES (
                1, 'TQQQ', '', 7,
                50, 55, 45, 60,
                0, '2026-08-17T14:00:00Z', 55, 2, 5,
                0, 0,
                '2026-08-18T14:00:00Z', '레거시 전량 청산', 'exit-old');
            """);

        await CreateMigrator(db).MigrateAsync();

        (await ScalarAsync<long>(connection,
            "SELECT InitialQuantity FROM Positions WHERE Symbol = 'TQQQ'"))
            .Should().Be(7);
        (await ScalarAsync<long>(connection,
            "SELECT ExitRequestQuantity FROM Positions WHERE Symbol = 'TQQQ'"))
            .Should().Be(7);
        (await ScalarAsync<long>(connection,
            "SELECT ExitRequestMarksPartialProfit FROM Positions WHERE Symbol = 'TQQQ'"))
            .Should().Be(0);
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

    private static async Task InsertVersionedStrategyAsync(
        SqliteConnection connection,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CustomPatterns (
                DocumentVersion, Name, EntryRulesJson, EntryLogic, RequireBullRegime,
                AtrStopMultiplier, AtrTargetMultiplier, MaxHoldingBars, TrailingAtr, PartialProfitR,
                UseWeightTiers, WeightTiersJson, DefaultAllocationPercent,
                ExitRulesJson, ExitRulesLogic, ExitGroupsJson, ExitGroupsLogic,
                ScalingRulesJson, TimeFilterJson, CircuitBreakerJson, ReentryJson,
                PortfolioRulesJson, EntryGroupsJson, EntryGroupsLogic, DynamicExitJson,
                EntryMode, TimeFrame, SizingMode, IsActive, EnableLiveTrading, CreatedAt, UpdatedAt)
            VALUES (
                1, $name, '[]', 'AND', 0,
                2, 3, 10, 0, 0,
                0, '[]', 100,
                '[]', 'OR', '[]', 'OR',
                '[]', '{}', '{}', '{}',
                '{}', '[]', 'AND', '{}',
                'CurrentClose', 0, 'FixedRisk', 1, 0,
                '2026-08-18T00:00:00Z', '2026-08-18T00:00:00Z');
            """;
        command.Parameters.AddWithValue("$name", name);
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
