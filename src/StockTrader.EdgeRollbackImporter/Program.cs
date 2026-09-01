using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using StockTrader.Domain.Strategies;
using StockTrader.Domain.Trading;
using StockTrader.ServiceContracts.TradingCore;
using StockTrader.TradingCore.Execution;

var sourcePath = Required("STOCKTRADER_EDGE_DATABASE_PATH");
var transferPath = Required("STOCKTRADER_FINANCIAL_TRANSFER_PATH");
var stagingPath = Required("STOCKTRADER_EDGE_STAGING_DATABASE_PATH");
var backupPath = Required("STOCKTRADER_EDGE_BACKUP_DATABASE_PATH");
var receiptPath = Required("STOCKTRADER_FINANCIAL_IMPORT_RECEIPT_PATH");
var transfer = JsonSerializer.Deserialize<CanonicalFinancialTransferV2>(
    await File.ReadAllTextAsync(transferPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
    ?? throw new InvalidDataException("empty-canonical-financial-transfer");
if (CanonicalFinancialTransferPolicy.Error(transfer) is { } transferError)
    throw new InvalidDataException(transferError);
if (transfer.Direction != AuthorityTransitionDirections.Rollback
    || transfer.SourceMode != TradingAuthorityMode.Remote)
    throw new InvalidDataException("illegal-rollback-transfer");

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(stagingPath))!);
await CheckpointAsync(sourcePath);
if (!File.Exists(backupPath))
    File.Copy(sourcePath, backupPath, overwrite: false);
File.Copy(sourcePath, stagingPath, overwrite: true);
var before = await NonFinancialInventoryAsync(stagingPath);

await using (var connection = new SqliteConnection(
    $"Data Source={stagingPath};Mode=ReadWrite;Pooling=False"))
{
    await connection.OpenAsync();
    await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
    await ExecuteAsync(connection, transaction, "DELETE FROM PositionScalingExecutions;DELETE FROM Positions;DELETE FROM TradeRecords;DELETE FROM TradeRecommendations;");

    foreach (var row in transfer.Recommendations)
    {
        var value = CanonicalFinancialTransferMapper.ReadRecommendation(row);
        await ExecuteAsync(connection, transaction, """
INSERT INTO TradeRecommendations
(Id,SourceSignalId,Symbol,PatternType,CustomPatternName,GeneratedAt,EntryPrice,StopLossPrice,
 TargetPrice,PositionSize,ShareQuantity,Expectancy,WasExecuted,IsSuperseded,Mode,
 EntryRequestedAt,EntryAccountId,EntryOrderId,EntryExecutionNote)
VALUES($id,$signal,$symbol,$pattern,$custom,$generated,$entry,$stop,$target,'0',$quantity,
 $expectancy,$executed,0,$mode,$requested,$account,$order,$note)
""",
            ("$id", long.Parse(value.RecommendationId)),
            ("$signal", Db(ParseNullableLong(value.SourceSignalId))),
            ("$symbol", value.Symbol), ("$pattern", (int)Enum.Parse<PatternType>(value.PatternCode)),
            ("$custom", Db(value.CustomPatternName)), ("$generated", Utc(value.GeneratedAtUtc)),
            ("$entry", D(value.EntryPrice)), ("$stop", D(value.StopLossPrice)),
            ("$target", D(value.TargetPrice)), ("$quantity", value.ShareQuantity),
            ("$expectancy", D(value.Expectancy)), ("$executed", value.WasExecuted ? 1 : 0),
            ("$mode", (int)Enum.Parse<OrderMode>(value.Mode)),
            ("$requested", Db(value.EntryRequestedAtUtc)),
            ("$account", Db(ParseNullableInt(value.EntryAccountId))),
            ("$order", Db(value.EntryOrderId)), ("$note", Db(value.EntryExecutionNote)));
    }
    foreach (var row in transfer.Positions)
    {
        var value = CanonicalFinancialTransferMapper.ReadPosition(row);
        await ExecuteAsync(connection, transaction, """
INSERT INTO Positions
(Id,SourceSignalId,AccountId,Symbol,Sector,Quantity,InitialQuantity,EntryPrice,CurrentPrice,
 StopLossPrice,TargetPrice,PatternType,CustomPatternName,OpenedAt,ClosedAt,ExitPrice,
 HighSinceEntry,EntryAtr,InitialRiskDistance,BreakevenApplied,TrailingStopActivated,
 PartialProfitTaken,ExitRequestedAt,ExitRequestReason,ExitRequestQuantity,
 ExitRequestMarksPartialProfit,ExecutionRequestKind,ExecutionRequestRuleIndex,ExitOrderId,
 ExecutionArtifactJson,EntryMarketDataEvidenceJson,LastEvaluatedEvidenceId,
 LastEvaluatedBarUtc,LastEvaluatedMarketDataRevision)
VALUES($id,$signal,$account,$symbol,$sector,$quantity,$initial,$entry,$current,$stop,$target,
 $pattern,$custom,$opened,$closed,$exit,$high,$atr,$risk,$breakeven,$trailing,$partial,
 $requested,$reason,$requestQuantity,$marksPartial,$kind,$rule,$order,$artifact,$evidence,
 $evidenceId,$bar,$revision)
""",
            ("$id", long.Parse(value.PositionId)), ("$signal", Db(ParseNullableLong(value.SourceSignalId))),
            ("$account", int.Parse(value.AccountId)), ("$symbol", value.Symbol), ("$sector", value.Sector),
            ("$quantity", value.Quantity), ("$initial", value.InitialQuantity),
            ("$entry", D(value.EntryPrice)), ("$current", D(value.CurrentPrice)),
            ("$stop", D(value.StopLossPrice)), ("$target", D(value.TargetPrice)),
            ("$pattern", (int)Enum.Parse<PatternType>(value.PatternCode)),
            ("$custom", Db(value.CustomPatternName)), ("$opened", Utc(value.OpenedAtUtc)),
            ("$closed", Db(value.ClosedAtUtc)), ("$exit", Db(value.ExitPrice is { } exit ? D(exit) : null)),
            ("$high", D(value.HighSinceEntry)), ("$atr", D(value.EntryAtr)),
            ("$risk", D(value.InitialRiskDistance)), ("$breakeven", value.BreakevenApplied ? 1 : 0),
            ("$trailing", value.TrailingStopActivated ? 1 : 0), ("$partial", value.PartialProfitTaken ? 1 : 0),
            ("$requested", Db(value.ExecutionRequestedAtUtc)), ("$reason", Db(value.ExecutionRequestReason)),
            ("$requestQuantity", Db(value.ExecutionRequestQuantity)),
            ("$marksPartial", value.ExecutionRequestMarksPartialProfit ? 1 : 0),
            ("$kind", Db(PositionExecutionKind(value.ExecutionRequestKind))),
            ("$rule", Db(value.ExecutionRequestRuleIndex)), ("$order", Db(value.ExecutionOrderId)),
            ("$artifact", Db(value.ExecutionContext is null ? null : JsonSerializer.Serialize(value.ExecutionContext.ExecutionArtifact))),
            ("$evidence", Db(value.ExecutionContext is null ? null : JsonSerializer.Serialize(value.ExecutionContext.EntryMarketDataEvidence))),
            ("$evidenceId", Db(value.LastEvaluatedEvidenceId)), ("$bar", Db(value.LastEvaluatedBarUtc)),
            ("$revision", value.LastEvaluatedMarketDataRevision));
        foreach (var scale in value.ScalingExecutions)
            await ExecuteAsync(connection, transaction, "INSERT INTO PositionScalingExecutions(PositionId,RuleIndex,ExecutionCount) VALUES($position,$rule,$count)",
                ("$position", long.Parse(value.PositionId)), ("$rule", scale.RuleIndex), ("$count", scale.ExecutionCount));
    }
    foreach (var row in transfer.RealizedTrades)
    {
        var value = CanonicalFinancialTransferMapper.ReadTrade(row);
        await ExecuteAsync(connection, transaction, """
INSERT INTO TradeRecords
(Id,SourceSignalId,Symbol,PatternType,CustomPatternName,EntryPrice,ExitPrice,Quantity,
 EntryTime,ExitTime,PnL,PnLPercent,ExitReason)
VALUES($id,$signal,$symbol,$pattern,$custom,$entry,$exit,$quantity,$entryTime,$exitTime,$pnl,$pct,$reason)
""",
            ("$id", long.Parse(value.TradeId)), ("$signal", Db(ParseNullableLong(value.SourceSignalId))),
            ("$symbol", value.Symbol), ("$pattern", (int)Enum.Parse<PatternType>(value.PatternCode)),
            ("$custom", Db(value.CustomPatternName)), ("$entry", D(value.EntryPrice)),
            ("$exit", D(value.ExitPrice)), ("$quantity", value.Quantity),
            ("$entryTime", Utc(value.EntryTimeUtc)), ("$exitTime", Utc(value.ExitTimeUtc)),
            ("$pnl", D(value.PnL)), ("$pct", D(value.PnLPercent)), ("$reason", value.ExitReason));
    }
    await transaction.CommitAsync();
}

var after = await NonFinancialInventoryAsync(stagingPath);
if (!string.Equals(before, after, StringComparison.Ordinal))
    throw new InvalidDataException("edge-nonfinancial-inventory-changed");
await QuickCheckAsync(stagingPath);
File.Move(stagingPath, sourcePath, overwrite: true);
var databaseHash = HashFile(sourcePath);
var importReceipt = new CanonicalFinancialImportReceipt(
    CanonicalFinancialTransferVersions.Current,
    transfer.TransferId,
    transfer.TransferHash,
    transfer.ReservedTargetGeneration,
    databaseHash,
    false,
    DateTime.UtcNow);
var temporaryReceipt = receiptPath + ".tmp";
await File.WriteAllTextAsync(temporaryReceipt, JsonSerializer.Serialize(importReceipt));
File.Move(temporaryReceipt, receiptPath, overwrite: true);
Console.WriteLine(JsonSerializer.Serialize(new
{
    status = "imported",
    transfer.TransferId,
    transfer.TransferHash,
    backupPath,
    backupHash = HashFile(backupPath),
    databaseHash,
}));

static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Missing required setting: {name}");
static long? ParseNullableLong(string? value) => long.TryParse(value, out var parsed) ? parsed : null;
static int? ParseNullableInt(string? value) => int.TryParse(value, out var parsed) ? parsed : null;
static object Db(object? value) => value switch
{
    null => DBNull.Value,
    DateTime instant => Utc(instant),
    _ => value,
};
static string Utc(DateTime value) => value.ToUniversalTime().ToString("O");
static string D(decimal value) => value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture);
static int? PositionExecutionKind(string? value) => value switch
{
    null or "" => null,
    "FullExit" => 0,
    "PartialProfit" => 1,
    "ScaleIn" => 2,
    "ScaleOut" => 3,
    _ => throw new InvalidDataException("unsupported-position-execution-kind"),
};
static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction,
    string sql, params (string Name, object Value)[] parameters)
{
    await using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    foreach (var parameter in parameters)
        command.Parameters.AddWithValue(parameter.Name, parameter.Value);
    await command.ExecuteNonQueryAsync();
}
static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

static async Task CheckpointAsync(string path)
{
    await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadWrite;Pooling=False");
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
    await command.ExecuteNonQueryAsync();
}

static async Task QuickCheckAsync(string path)
{
    await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA quick_check;";
    if (!string.Equals(Convert.ToString(await command.ExecuteScalarAsync()), "ok", StringComparison.Ordinal))
        throw new InvalidDataException("edge-staging-database-integrity-failed");
}

static async Task<string> NonFinancialInventoryAsync(string path)
{
    var excluded = new HashSet<string>(StringComparer.Ordinal)
    {
        "TradeRecommendations", "Positions", "PositionScalingExecutions", "TradeRecords",
        "FinancialAuthorityFences", "FinancialAuthorityMirrors", "sqlite_sequence"
    };
    await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
    await connection.OpenAsync();
    var inventory = new SortedDictionary<string, string>(StringComparer.Ordinal);
    await using var tables = connection.CreateCommand();
    tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
    await using var reader = await tables.ExecuteReaderAsync();
    var names = new List<string>();
    while (await reader.ReadAsync()) names.Add(reader.GetString(0));
    await reader.CloseAsync();
    foreach (var name in names.Where(value => !excluded.Contains(value)))
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{name.Replace("\"", "\"\"")}\" ORDER BY rowid";
        await using var rows = await command.ExecuteReaderAsync();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long count = 0;
        while (await rows.ReadAsync())
        {
            count++;
            for (var index = 0; index < rows.FieldCount; index++)
            {
                var bytes = rows.IsDBNull(index) ? [byte.MaxValue] : rows.GetValue(index) switch
                {
                    byte[] binary => binary,
                    object value => Encoding.UTF8.GetBytes(Convert.ToString(value,
                        System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty),
                };
                hash.AppendData(bytes);
                hash.AppendData([0]);
            }
        }
        inventory[name] = $"{count}:{Convert.ToHexString(hash.GetHashAndReset())}";
    }
    return JsonSerializer.Serialize(inventory);
}
