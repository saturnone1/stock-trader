namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreAuthorityStore =
    type TradingCoreStore with
        member this.Authority() =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT mode,generation,authority_id,activated_at,previous_state_hash,broker_reconciliation_hash,broker_reconciled_at,unresolved_broker_orders FROM authority WHERE singleton=1"
            use reader = command.ExecuteReader()
            if not (reader.Read()) then invalidOp "missing-authority-state"
            TradingAuthorityContract(TradingCoreContractVersions.Current,
                Enum.Parse<TradingAuthorityMode>(reader.GetString 0), reader.GetInt64 1,
                reader.GetString 2, DateTime.Parse(reader.GetString 3, null,
                    Globalization.DateTimeStyles.RoundtripKind), reader.GetString 4,
                reader.GetString 5,
                (if reader.IsDBNull 6 then Nullable() else Nullable(DateTime.Parse(
                    reader.GetString 6, null, Globalization.DateTimeStyles.RoundtripKind))),
                reader.GetInt32 7)
    
        member this.Import(snapshot: TradingStateSnapshot) =
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error snapshot) with
            | Some error -> invalidArg "snapshot" error
            | None ->
                let authority = this.Authority()
                if authority.Mode <> TradingAuthorityMode.Projection
                    && authority.Mode <> TradingAuthorityMode.Shadow then
                    invalidOp "snapshot-import-disabled-for-authority-mode"
                use connection = this.Connect()
                use transaction = connection.BeginTransaction()
                use existing = connection.CreateCommand()
                existing.Transaction <- transaction
                existing.CommandText <- "SELECT source_generation FROM snapshots WHERE snapshot_id=$id"
                existing.Parameters.AddWithValue("$id", snapshot.SnapshotId) |> ignore
                match existing.ExecuteScalar() with
                | null ->
                    use insert = connection.CreateCommand()
                    insert.Transaction <- transaction
                    insert.CommandText <- "INSERT INTO snapshots VALUES($id,$generation,$captured,$payload,$accepted)"
                    insert.Parameters.AddWithValue("$id", snapshot.SnapshotId) |> ignore
                    insert.Parameters.AddWithValue("$generation", snapshot.SourceGeneration) |> ignore
                    insert.Parameters.AddWithValue("$captured", snapshot.CapturedAtUtc.ToString("O")) |> ignore
                    insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot, this.Json)) |> ignore
                    insert.Parameters.AddWithValue("$accepted", DateTime.UtcNow.ToString("O")) |> ignore
                    insert.ExecuteNonQuery() |> ignore
                    use clear = connection.CreateCommand()
                    clear.Transaction <- transaction
                    clear.CommandText <- "DELETE FROM projections"
                    clear.ExecuteNonQuery() |> ignore
                    let rows =
                        [ "account", snapshot.Accounts |> Seq.map (fun x -> x.AccountId, box x)
                          "recommendation", snapshot.Recommendations |> Seq.map (fun x -> x.RecommendationId, box x)
                          "position", snapshot.Positions |> Seq.map (fun x -> x.PositionId, box x)
                          "trade", snapshot.Trades |> Seq.map (fun x -> x.TradeId, box x) ]
                    for kind, items in rows do
                        for identity, item in items do
                            use projection = connection.CreateCommand()
                            projection.Transaction <- transaction
                            projection.CommandText <- "INSERT INTO projections VALUES($kind,$id,$payload,$snapshot)"
                            projection.Parameters.AddWithValue("$kind", kind) |> ignore
                            projection.Parameters.AddWithValue("$id", identity) |> ignore
                            projection.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, this.Json)) |> ignore
                            projection.Parameters.AddWithValue("$snapshot", snapshot.SnapshotId) |> ignore
                            projection.ExecuteNonQuery() |> ignore
                    use state = connection.CreateCommand()
                    state.Transaction <- transaction
                    state.CommandText <- "UPDATE state SET value=$id WHERE key='last_snapshot_id'"
                    state.Parameters.AddWithValue("$id", snapshot.SnapshotId) |> ignore
                    state.ExecuteNonQuery() |> ignore
                    transaction.Commit()
                    false
                | generation ->
                    if Convert.ToInt64(generation) <> snapshot.SourceGeneration then
                        invalidOp "snapshot-id-generation-conflict"
                    transaction.Rollback()
                    true
    
        member this.ApplyAccountConfiguration(configuration: TradingAccountConfigurationSet) =
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error configuration) with
            | Some error -> invalidArg "configuration" error
            | None ->
                use connection = this.Connect()
                let current = Int64.Parse(this.StateValue(connection, "account_generation"))
                use transaction = connection.BeginTransaction()
                if configuration.Generation < current then invalidOp "stale-account-generation"
                use existing = connection.CreateCommand()
                existing.Transaction <- transaction
                existing.CommandText <- "SELECT configuration_hash FROM account_configuration WHERE singleton=1"
                let existingHash = existing.ExecuteScalar()
                if configuration.Generation = current then
                    if isNull existingHash
                       || Convert.ToString(existingHash) <> configuration.ConfigurationHash then
                        invalidOp "account-generation-conflict"
                    transaction.Rollback()
                    TradingAccountConfigurationReceipt(TradingCoreContractVersions.Current,
                        configuration.Generation, configuration.ConfigurationHash, true)
                else
                    let protectedPayload = this.Secrets.Protect(JsonSerializer.Serialize(configuration, this.Json))
                    use upsert = connection.CreateCommand()
                    upsert.Transaction <- transaction
                    upsert.CommandText <- """INSERT INTO account_configuration
    (singleton,generation,configuration_hash,ciphertext,nonce,tag,accepted_at)
    VALUES(1,$generation,$hash,$ciphertext,$nonce,$tag,$at)
    ON CONFLICT(singleton) DO UPDATE SET generation=excluded.generation,
    configuration_hash=excluded.configuration_hash,ciphertext=excluded.ciphertext,
    nonce=excluded.nonce,tag=excluded.tag,accepted_at=excluded.accepted_at"""
                    upsert.Parameters.AddWithValue("$generation", configuration.Generation) |> ignore
                    upsert.Parameters.AddWithValue("$hash", configuration.ConfigurationHash) |> ignore
                    upsert.Parameters.AddWithValue("$ciphertext", protectedPayload.Ciphertext) |> ignore
                    upsert.Parameters.AddWithValue("$nonce", protectedPayload.Nonce) |> ignore
                    upsert.Parameters.AddWithValue("$tag", protectedPayload.Tag) |> ignore
                    upsert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
                    upsert.ExecuteNonQuery() |> ignore
                    use state = connection.CreateCommand()
                    state.Transaction <- transaction
                    state.CommandText <- "UPDATE state SET value=$generation WHERE key='account_generation'"
                    state.Parameters.AddWithValue("$generation", configuration.Generation.ToString()) |> ignore
                    state.ExecuteNonQuery() |> ignore
                    transaction.Commit()
                    TradingAccountConfigurationReceipt(TradingCoreContractVersions.Current,
                        configuration.Generation, configuration.ConfigurationHash, false)
    
        member this.Activate(next: TradingAuthorityContract) =
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error next) with
            | Some error -> invalidArg "authority" error
            | None ->
                let current = this.Authority()
                if next.Generation <> current.Generation + 1L then invalidOp "non-monotonic-authority-generation"
                if next.Mode = TradingAuthorityMode.Remote then
                    if current.Mode <> TradingAuthorityMode.Shadow then invalidOp "remote-requires-shadow"
                    use check = this.Connect()
                    if next.PreviousStateHash <> this.StateValue(check, "last_snapshot_id") then
                        invalidOp "cutover-state-hash-mismatch"
                    if next.BrokerReconciledAtUtc.Value < DateTime.UtcNow.AddMinutes(-5) then
                        invalidOp "stale-broker-reconciliation"
                use connection = this.Connect()
                use transaction = connection.BeginTransaction()
                if next.Mode = TradingAuthorityMode.Remote then
                    use materialize = connection.CreateCommand()
                    materialize.Transaction <- transaction
                    use projectedPositions = connection.CreateCommand()
                    projectedPositions.Transaction <- transaction
                    projectedPositions.CommandText <- "SELECT payload_json FROM projections WHERE kind='position'"
                    use positionReader = projectedPositions.ExecuteReader()
                    let mutable missingExecutionContext = false
                    while positionReader.Read() do
                        let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                            positionReader.GetString 0, this.Json)
                        if isNull position || (not position.ClosedAtUtc.HasValue
                            && isNull position.ExecutionContext) then
                            missingExecutionContext <- true
                    positionReader.Close()
                    if missingExecutionContext then
                        invalidOp "open-position-execution-context-missing"
                    materialize.CommandText <- """
    DELETE FROM canonical_recommendations;
    DELETE FROM canonical_positions;
    DELETE FROM canonical_trades;
    INSERT INTO canonical_recommendations(identity,source_signal_id,payload_json,status,broker_order_id,version)
     SELECT identity,json_extract(payload_json,'$.sourceSignalId'),payload_json,'Imported',json_extract(payload_json,'$.entryOrderId'),1
     FROM projections WHERE kind='recommendation';
    INSERT INTO canonical_positions(identity,source_signal_id,payload_json,execution_context_json,version)
     SELECT identity,json_extract(payload_json,'$.sourceSignalId'),payload_json,
            COALESCE(json_extract(payload_json,'$.executionContext'),'{}'),1
     FROM projections WHERE kind='position';
    INSERT INTO canonical_trades(identity,payload_json,version)
     SELECT identity,payload_json,1 FROM projections WHERE kind='trade';
    """
                    materialize.ExecuteNonQuery() |> ignore
                use command = connection.CreateCommand()
                command.Transaction <- transaction
                command.CommandText <- """UPDATE authority SET mode=$mode,generation=$generation,
    authority_id=$id,activated_at=$at,previous_state_hash=$hash,
    broker_reconciliation_hash=$reconciliation,broker_reconciled_at=$reconciled,
    unresolved_broker_orders=$unresolved WHERE singleton=1 AND generation=$previous"""
                command.Parameters.AddWithValue("$mode", next.Mode.ToString()) |> ignore
                command.Parameters.AddWithValue("$generation", next.Generation) |> ignore
                command.Parameters.AddWithValue("$id", next.AuthorityId) |> ignore
                command.Parameters.AddWithValue("$at", next.ActivatedAtUtc.ToString("O")) |> ignore
                command.Parameters.AddWithValue("$hash", next.PreviousStateHash) |> ignore
                command.Parameters.AddWithValue("$reconciliation", next.BrokerReconciliationHash) |> ignore
                command.Parameters.AddWithValue("$reconciled",
                    if next.BrokerReconciledAtUtc.HasValue then box (next.BrokerReconciledAtUtc.Value.ToString("O")) else DBNull.Value) |> ignore
                command.Parameters.AddWithValue("$unresolved", next.UnresolvedBrokerOrders) |> ignore
                command.Parameters.AddWithValue("$previous", current.Generation) |> ignore
                if command.ExecuteNonQuery() <> 1 then invalidOp "authority-generation-race"
                transaction.Commit()
