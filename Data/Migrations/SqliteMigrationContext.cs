using System.Data.Common;

namespace StockTrader.Data.Migrations;

public sealed class SqliteMigrationContext
{
    public DbConnection Connection { get; }
    public DbTransaction Transaction { get; }

    public SqliteMigrationContext(DbConnection connection, DbTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct = default)
    {
        await using var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken ct = default)
    {
        await using var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(ct);
    }

    public async Task<bool> TableExistsAsync(string table, CancellationToken ct = default) =>
        await ScalarAsync(
            $"SELECT 1 FROM sqlite_master WHERE type='table' AND name={Literal(table)} LIMIT 1",
            ct) is not null;

    public async Task<IReadOnlySet<string>> GetColumnsAsync(string table, CancellationToken ct = default)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = $"PRAGMA table_info({Identifier(table)})";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
        return columns;
    }

    public async Task EnsureColumnsAsync(
        string table,
        IReadOnlyDictionary<string, string> definitions,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(table, ct)) return;
        var columns = await GetColumnsAsync(table, ct);
        foreach (var (name, definition) in definitions)
        {
            if (columns.Contains(name)) continue;
            await ExecuteAsync(
                $"ALTER TABLE {Identifier(table)} ADD COLUMN {Identifier(name)} {definition}",
                ct);
        }
    }

    public async Task EnsureTableAsync(
        string table,
        string createSql,
        IEnumerable<string>? indexSql = null,
        CancellationToken ct = default)
    {
        if (!await TableExistsAsync(table, ct)) await ExecuteAsync(createSql, ct);
        if (indexSql is null) return;
        foreach (var sql in indexSql) await ExecuteAsync(sql, ct);
    }

    private static string Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("SQLite 식별자에 허용되지 않는 문자가 있습니다.", nameof(value));
        return $"\"{value}\"";
    }

    private static string Literal(string value) => $"'{value.Replace("'", "''")}'";
}
