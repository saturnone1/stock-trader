using System.Data;
using Microsoft.EntityFrameworkCore;

namespace StockTrader.Data.Migrations;

/// <summary>
/// Applies EF Core migrations and rejects databases that never adopted EF history.
/// Legacy schema mutation has been retired; an old database must be upgraded with a historical
/// release or restored from a verified backup before this application can open it.
/// </summary>
public sealed class DatabaseSchemaMigrator
{
    internal const string InitialMigrationSuffix = "_InitialSchema";
    private readonly AppDbContext _db;
    private readonly ILogger<DatabaseSchemaMigrator> _logger;

    public DatabaseSchemaMigrator(
        AppDbContext db,
        ILogger<DatabaseSchemaMigrator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        if (!_db.Database.GetMigrations().Any(value =>
                value.EndsWith(InitialMigrationSuffix, StringComparison.Ordinal)))
            throw new InvalidOperationException("EF 초기 스키마 마이그레이션을 찾을 수 없습니다.");

        var applied = await _db.Database.GetAppliedMigrationsAsync(ct);
        if (!applied.Any() && await HasApplicationTablesAsync(ct))
        {
            throw new InvalidOperationException(
                "EF 마이그레이션 이력이 없는 기존 데이터베이스는 자동 변경하지 않습니다. " +
                "EF 기준선을 채택한 이전 릴리스로 먼저 업그레이드하거나 검증된 백업을 복원하세요.");
        }

        _logger.LogInformation("Ensuring database schema is synchronized with EF Core migrations");
        await _db.Database.MigrateAsync(ct);
    }

    private async Task<bool> HasApplicationTablesAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
              AND name NOT IN ('__EFMigrationsHistory', '__StockTraderMigrations')
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
    }

}
