using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StockTrader.Data;
using StockTrader.Data.Migrations;

namespace StockTrader.Tests;

public class DatabaseMigrationRunnerTests
{
    [Fact]
    public async Task SchemaMigrator_EmptyDatabaseIsCreatedOnlyByEfMigrations()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var migrator = CreateSchemaMigrator(db);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        (await TableExistsAsync(connection, "OhlcvBars")).Should().BeTrue();
        (await TableExistsAsync(connection, "CustomPatterns")).Should().BeTrue();
        (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeTrue();
        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM __EFMigrationsHistory")).Should().Be(1);
        (await TableExistsAsync(connection, DatabaseMigrationRunner.HistoryTable)).Should().BeFalse(
            "새 데이터베이스는 레거시 보정기를 거치면 안 됩니다");
    }

    [Fact]
    public async Task SchemaMigrator_CurrentLegacyDatabasePreservesRowsAndAdoptsInitialEfBaseline()
    {
        await using var connection = await OpenConnectionAsync();
        await using (var setup = CreateContext(connection))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Positions.Add(new StockTrader.Models.Position
            {
                Symbol = "TQQQ",
                EntryPrice = 100m,
                CurrentPrice = 101m,
                StopLossPrice = 95m,
                TargetPrice = 110m,
                Quantity = 3
            });
            await setup.SaveChangesAsync();
        }
        await using var db = CreateContext(connection);

        await CreateSchemaMigrator(db).MigrateAsync();

        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM Positions WHERE Symbol = 'TQQQ' AND Quantity = 3")).Should().Be(1);
        (await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM \"{DatabaseMigrationRunner.HistoryTable}\"")).Should().Be(3);
        (await ScalarAsync<string>(connection,
            "SELECT MigrationId FROM __EFMigrationsHistory LIMIT 1"))
            .Should().EndWith(DatabaseSchemaMigrator.InitialMigrationSuffix);
    }

    [Fact]
    public async Task SchemaMigrator_IncompleteLegacyDatabaseRefusesToForgeEfHistory()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE Positions (Id INTEGER PRIMARY KEY AUTOINCREMENT, Symbol TEXT NOT NULL DEFAULT '');
            INSERT INTO Positions (Symbol) VALUES ('TQQQ');
            """);
        await using var db = CreateContext(connection);

        var action = () => CreateSchemaMigrator(db).MigrateAsync();

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*EF 초기 스키마와 일치하지 않아*");
        (await TableExistsAsync(connection, "__EFMigrationsHistory")).Should().BeFalse();
        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM Positions WHERE Symbol = 'TQQQ'")).Should().Be(1);
    }

    [Fact]
    public async Task MigrateAsync_EmptyDatabaseCreatesCurrentSchemaAndRunsOnlyOnce()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var baseline = new LegacySchemaBaselineMigration(NullLogger<LegacySchemaBaselineMigration>.Instance);
        var runner = CreateRunner(db, baseline);

        await runner.MigrateAsync();
        await runner.MigrateAsync();

        (await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM \"{DatabaseMigrationRunner.HistoryTable}\"")).Should().Be(1);
        (await TableExistsAsync(connection, "CustomPatterns")).Should().BeTrue();
        (await TableExistsAsync(connection, "OptimizationResults")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_LegacyPartialSchemaPreservesRowsAndAddsMissingColumns()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE UserSettings (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderMode INTEGER NOT NULL DEFAULT 0);
            INSERT INTO UserSettings (OrderMode) VALUES (1);
            CREATE TABLE Positions (Id INTEGER PRIMARY KEY AUTOINCREMENT, Symbol TEXT NOT NULL DEFAULT '');
            INSERT INTO Positions (Symbol) VALUES ('TQQQ');
            """);
        await using var db = CreateContext(connection);
        var runner = CreateRunner(db,
            new LegacySchemaBaselineMigration(NullLogger<LegacySchemaBaselineMigration>.Instance));

        await runner.MigrateAsync();

        (await ColumnsAsync(connection, "UserSettings")).Should().Contain([
            "RiskPerTradePercent", "LiveParameterOverridesJson", "Tqqq200SmaAllowedSymbols"]);
        (await ColumnsAsync(connection, "Positions")).Should().Contain(["HighSinceEntry", "EntryAtr", "AccountId"]);
        (await ScalarAsync<long>(connection, "SELECT COUNT(*) FROM UserSettings")).Should().Be(1);
        (await ScalarAsync<string>(connection, "SELECT Symbol FROM Positions LIMIT 1")).Should().Be("TQQQ");
    }

    [Fact]
    public async Task MigrateAsync_AddsDurablePositionExecutionStateWithoutChangingRows()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE Positions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol TEXT NOT NULL DEFAULT '',
                StopLossPrice REAL NOT NULL DEFAULT 0);
            INSERT INTO Positions (Symbol, StopLossPrice) VALUES ('TQQQ', 42.5);
            """);
        await using var db = CreateContext(connection);
        var migration = new PositionExecutionStateMigration();

        await CreateRunner(db, migration).MigrateAsync();

        (await ColumnsAsync(connection, "Positions")).Should().Contain([
            "InitialRiskDistance", "BreakevenApplied", "TrailingStopActivated"]);
        (await ScalarAsync<decimal>(connection,
            "SELECT StopLossPrice FROM Positions WHERE Symbol = 'TQQQ'")).Should().Be(42.5m);
        (await ScalarAsync<long>(connection,
            "SELECT BreakevenApplied FROM Positions WHERE Symbol = 'TQQQ'")).Should().Be(0);
    }

    [Fact]
    public async Task PositionExecutionState_RoundTripsThroughCurrentEfModel()
    {
        await using var connection = await OpenConnectionAsync();
        await using (var writeDb = CreateContext(connection))
        {
            await writeDb.Database.EnsureCreatedAsync();
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

    [Fact]
    public async Task MigrateAsync_AddsDurableExitIntentWithoutChangingOpenPosition()
    {
        await using var connection = await OpenConnectionAsync();
        await ExecuteAsync(connection, """
            CREATE TABLE Positions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Symbol TEXT NOT NULL DEFAULT '',
                ClosedAt TEXT);
            INSERT INTO Positions (Symbol, ClosedAt) VALUES ('TQQQ', NULL);
            """);
        await using var db = CreateContext(connection);

        await CreateRunner(db, new PositionExitIntentMigration()).MigrateAsync();

        (await ColumnsAsync(connection, "Positions")).Should().Contain([
            "ExitRequestedAt", "ExitRequestReason", "ExitOrderId"]);
        (await ScalarAsync<long>(connection,
            "SELECT COUNT(*) FROM Positions WHERE Symbol = 'TQQQ' AND ClosedAt IS NULL"))
            .Should().Be(1);
    }

    [Fact]
    public async Task MigrateAsync_SortsMigrationsByIdEvenWhenRegistrationOrderDiffers()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var second = new DelegateMigration("002_second", async (context, ct) =>
            await context.ExecuteAsync("INSERT INTO MigrationOrder (Value) VALUES ('second')", ct));
        var first = new DelegateMigration("001_first", async (context, ct) =>
        {
            await context.ExecuteAsync("CREATE TABLE MigrationOrder (Position INTEGER PRIMARY KEY AUTOINCREMENT, Value TEXT NOT NULL)", ct);
            await context.ExecuteAsync("INSERT INTO MigrationOrder (Value) VALUES ('first')", ct);
        });

        await CreateRunner(db, second, first).MigrateAsync();

        (await ReadStringsAsync(connection, "SELECT Value FROM MigrationOrder ORDER BY Position"))
            .Should().Equal("first", "second");
    }

    [Fact]
    public async Task MigrateAsync_FailureRollsBackSchemaAndDoesNotRecordVersion()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var failing = new DelegateMigration("001_failing", async (context, ct) =>
        {
            await context.ExecuteAsync("CREATE TABLE MustRollback (Id INTEGER PRIMARY KEY)", ct);
            throw new InvalidOperationException("expected failure");
        });

        var action = () => CreateRunner(db, failing).MigrateAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
        (await TableExistsAsync(connection, "MustRollback")).Should().BeFalse();
        (await ScalarAsync<long>(connection,
            $"SELECT COUNT(*) FROM \"{DatabaseMigrationRunner.HistoryTable}\"")).Should().Be(0);
    }

    private static DatabaseMigrationRunner CreateRunner(AppDbContext db, params IDatabaseMigration[] migrations) =>
        new(db, migrations, NullLogger<DatabaseMigrationRunner>.Instance);

    private static DatabaseSchemaMigrator CreateSchemaMigrator(AppDbContext db)
    {
        var legacy = CreateRunner(
            db,
            new LegacySchemaBaselineMigration(NullLogger<LegacySchemaBaselineMigration>.Instance),
            new PositionExecutionStateMigration(),
            new PositionExitIntentMigration());
        return new DatabaseSchemaMigrator(
            db,
            legacy,
            NullLogger<DatabaseSchemaMigrator>.Instance);
    }

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

    private static async Task<HashSet<string>> ColumnsAsync(SqliteConnection connection, string table)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(1));
        return result;
    }

    private static async Task<List<string>> ReadStringsAsync(SqliteConnection connection, string sql)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    private sealed class DelegateMigration : IDatabaseMigration
    {
        private readonly Func<SqliteMigrationContext, CancellationToken, Task> _apply;
        public string Id { get; }
        public string Description => Id;

        public DelegateMigration(string id, Func<SqliteMigrationContext, CancellationToken, Task> apply)
        {
            Id = id;
            _apply = apply;
        }

        public Task ApplyAsync(SqliteMigrationContext context, CancellationToken ct) => _apply(context, ct);
    }
}
