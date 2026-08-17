using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace StockTrader.Data.Migrations;

/// <summary>
/// Proves that a legacy SQLite database already contains every table, column, and named index in
/// the EF initial model before its migration history is baselined.
/// </summary>
public sealed class EfBaselineCompatibilityValidator
{
    private readonly AppDbContext _db;

    public EfBaselineCompatibilityValidator(AppDbContext db)
    {
        _db = db;
    }

    public async Task<EfBaselineCompatibility> ValidateAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var missingTables = new List<string>();
        var missingColumns = new List<string>();
        var missingIndexes = new List<string>();
        foreach (var table in _db.Model.GetRelationalModel().Tables.OrderBy(value => value.Name))
        {
            if (!await TableExistsAsync(connection, table.Name, ct))
            {
                missingTables.Add(table.Name);
                continue;
            }

            var columns = await ReadNamesAsync(
                connection,
                $"PRAGMA table_info({Identifier(table.Name)})",
                nameOrdinal: 1,
                ct);
            missingColumns.AddRange(table.Columns
                .Where(column => !columns.Contains(column.Name))
                .Select(column => $"{table.Name}.{column.Name}"));

            var indexes = await ReadNamesAsync(
                connection,
                $"PRAGMA index_list({Identifier(table.Name)})",
                nameOrdinal: 1,
                ct);
            missingIndexes.AddRange(table.Indexes
                .Where(index => !indexes.Contains(index.Name))
                .Select(index => $"{table.Name}.{index.Name}"));
        }

        return new EfBaselineCompatibility(missingTables, missingColumns, missingIndexes);
    }

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        string table,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<HashSet<string>> ReadNamesAsync(
        System.Data.Common.DbConnection connection,
        string commandText,
        int nameOrdinal,
        CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            names.Add(reader.GetString(nameOrdinal));
        return names;
    }

    private static string Identifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}

public sealed record EfBaselineCompatibility(
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> MissingColumns,
    IReadOnlyList<string> MissingIndexes)
{
    public bool IsCompatible =>
        MissingTables.Count == 0 && MissingColumns.Count == 0 && MissingIndexes.Count == 0;

    public string Describe() => string.Join("; ", new[]
    {
        MissingTables.Count == 0 ? null : $"tables=[{string.Join(", ", MissingTables)}]",
        MissingColumns.Count == 0 ? null : $"columns=[{string.Join(", ", MissingColumns)}]",
        MissingIndexes.Count == 0 ? null : $"indexes=[{string.Join(", ", MissingIndexes)}]"
    }.Where(value => value != null));
}
