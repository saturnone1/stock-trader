using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Migrations;

namespace StockTrader.Tests;

public class DatabaseMigrationStatusProviderTests
{
    [Fact]
    public async Task GetAsync_ReportsTheAppliedInitialSchemaAsSynchronized()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();

        var status = await new DatabaseMigrationStatusProvider(db).GetAsync();

        status.Current.Should().EndWith(DatabaseSchemaMigrator.InitialMigrationSuffix);
        status.Latest.Should().Be(status.Current);
        status.PendingCount.Should().Be(0);
        status.IsSynchronized.Should().BeTrue();
    }

    [Fact]
    public async Task GetAsync_ReportsPendingSchemaBeforeMigration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);

        var status = await new DatabaseMigrationStatusProvider(db).GetAsync();

        status.Current.Should().BeNull();
        status.Latest.Should().EndWith(DatabaseSchemaMigrator.InitialMigrationSuffix);
        status.PendingCount.Should().Be(1);
        status.IsSynchronized.Should().BeFalse();
    }

    private static AppDbContext Context(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
}
