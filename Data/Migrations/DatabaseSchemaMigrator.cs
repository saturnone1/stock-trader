using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace StockTrader.Data.Migrations;

/// <summary>
/// Owns the one-time transition from the legacy migration ledger to EF Core migrations.
/// Empty databases are created by EF. Existing databases are compatibility-upgraded, structurally
/// verified, and only then marked at the initial EF baseline.
/// </summary>
public sealed class DatabaseSchemaMigrator
{
    internal const string InitialMigrationSuffix = "_InitialSchema";
    internal const string InitialProductVersion = "10.0.10";

    private readonly AppDbContext _db;
    private readonly DatabaseMigrationRunner _legacyMigrations;
    private readonly ILogger<DatabaseSchemaMigrator> _logger;

    public DatabaseSchemaMigrator(
        AppDbContext db,
        DatabaseMigrationRunner legacyMigrations,
        ILogger<DatabaseSchemaMigrator> logger)
    {
        _db = db;
        _legacyMigrations = legacyMigrations;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var efMigrations = _db.Database.GetMigrations().ToArray();
        var initialMigration = efMigrations.FirstOrDefault(value =>
            value.EndsWith(InitialMigrationSuffix, StringComparison.Ordinal));
        if (initialMigration == null)
            throw new InvalidOperationException("EF 초기 스키마 마이그레이션을 찾을 수 없습니다.");

        var applied = (await _db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet(StringComparer.Ordinal);
        if (applied.Count > 0)
        {
            await _db.Database.MigrateAsync(ct);
            return;
        }

        if (!await HasApplicationTablesAsync(ct))
        {
            _logger.LogInformation("Creating a new database from EF Core migrations");
            await _db.Database.MigrateAsync(ct);
            return;
        }

        _logger.LogInformation("Upgrading legacy database before adopting EF Core migration history");
        await _legacyMigrations.MigrateAsync(ct);
        var compatibility = await new EfBaselineCompatibilityValidator(_db).ValidateAsync(ct);
        if (!compatibility.IsCompatible)
        {
            throw new InvalidOperationException(
                $"기존 데이터베이스가 EF 초기 스키마와 일치하지 않아 기준선을 등록할 수 없습니다: {compatibility.Describe()}");
        }

        await RecordInitialBaselineAsync(initialMigration, ct);
        await _db.Database.MigrateAsync(ct);
        _logger.LogInformation("Adopted EF Core migration baseline {MigrationId}", initialMigration);
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

    private async Task RecordInitialBaselineAsync(string migrationId, CancellationToken ct)
    {
        var history = _db.GetService<IHistoryRepository>();
        await _db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);
        var applied = await _db.Database.GetAppliedMigrationsAsync(ct);
        if (applied.Contains(migrationId, StringComparer.Ordinal))
            return;
        await _db.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(migrationId, InitialProductVersion)),
            ct);
    }
}
