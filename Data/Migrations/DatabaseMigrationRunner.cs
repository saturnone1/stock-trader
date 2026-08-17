using System.Data;
using Microsoft.EntityFrameworkCore;

namespace StockTrader.Data.Migrations;

public sealed class DatabaseMigrationRunner
{
    public const string HistoryTable = "__StockTraderMigrations";

    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IDatabaseMigration> _migrations;
    private readonly ILogger<DatabaseMigrationRunner> _logger;

    public DatabaseMigrationRunner(
        AppDbContext db,
        IEnumerable<IDatabaseMigration> migrations,
        ILogger<DatabaseMigrationRunner> logger)
    {
        _db = db;
        _migrations = migrations.OrderBy(migration => migration.Id, StringComparer.Ordinal).ToArray();
        _logger = logger;

        var duplicate = _migrations.GroupBy(migration => migration.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"중복 데이터베이스 마이그레이션 ID: {duplicate.Key}");
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await _db.Database.EnsureCreatedAsync(ct);
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(ct);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS "{HistoryTable}" (
                    Id TEXT NOT NULL PRIMARY KEY,
                    Description TEXT NOT NULL,
                    AppliedAtUtc TEXT NOT NULL
                )
                """;
            await command.ExecuteNonQueryAsync(ct);
        }

        var applied = await ReadAppliedIdsAsync(connection, ct);
        foreach (var migration in _migrations.Where(migration => !applied.Contains(migration.Id)))
        {
            _logger.LogInformation("Applying database migration {MigrationId}: {Description}", migration.Id, migration.Description);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                var context = new SqliteMigrationContext(connection, transaction);
                await migration.ApplyAsync(context, ct);
                await using var record = connection.CreateCommand();
                record.Transaction = transaction;
                record.CommandText = $"""
                    INSERT INTO "{HistoryTable}" (Id, Description, AppliedAtUtc)
                    VALUES ($id, $description, $appliedAtUtc)
                    """;
                AddParameter(record, "$id", migration.Id);
                AddParameter(record, "$description", migration.Description);
                AddParameter(record, "$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
                await record.ExecuteNonQueryAsync(ct);
                await transaction.CommitAsync(ct);
                _logger.LogInformation("Applied database migration {MigrationId}", migration.Id);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private static async Task<HashSet<string>> ReadAppliedIdsAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id FROM \"{HistoryTable}\"";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
