using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockTrader.Data;
using StockTrader.Data.Migrations;

namespace StockTrader.Tests;

public class DatabaseMigrationStatusProviderTests
{
    [Fact]
    public async Task GetAsync_ReportsAllAppliedMigrationsAsSynchronized()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Context(connection);
        await db.Database.MigrateAsync();
        var latest = db.Database.GetMigrations().Last();

        var status = await new DatabaseMigrationStatusProvider(db).GetAsync();

        status.Current.Should().Be(latest);
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
        var migrations = db.Database.GetMigrations().ToArray();

        var status = await new DatabaseMigrationStatusProvider(db).GetAsync();

        status.Current.Should().BeNull();
        status.Latest.Should().Be(migrations[^1]);
        status.PendingCount.Should().Be(migrations.Length);
        status.IsSynchronized.Should().BeFalse();
    }

    private static AppDbContext Context(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
}
