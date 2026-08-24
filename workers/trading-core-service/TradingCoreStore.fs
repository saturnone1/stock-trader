namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

type TradingCoreStore(config: ServiceConfig, json: JsonSerializerOptions, secrets: SecretStore) =
    do Database.initialize config.DatabasePath config.InitialMode
    let connect () = Database.connect config.DatabasePath
    let value (connection: SqliteConnection) key =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM state WHERE key=$key"
        command.Parameters.AddWithValue("$key", key) |> ignore
        Convert.ToString(command.ExecuteScalar())

    member _.Authority() =
        use connection = connect()
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
            use connection = connect()
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
                insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot, json)) |> ignore
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
                        projection.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, json)) |> ignore
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

    member _.ApplyAccountConfiguration(configuration: TradingAccountConfigurationSet) =
        match Option.ofObj (TradingCoreCompatibilityPolicy.Error configuration) with
        | Some error -> invalidArg "configuration" error
        | None ->
            use connection = connect()
            let current = Int64.Parse(value connection "account_generation")
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
                let protectedPayload = secrets.Protect(JsonSerializer.Serialize(configuration, json))
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
                use check = connect()
                if next.PreviousStateHash <> value check "last_snapshot_id" then
                    invalidOp "cutover-state-hash-mismatch"
                if next.BrokerReconciledAtUtc.Value < DateTime.UtcNow.AddMinutes(-5) then
                    invalidOp "stale-broker-reconciliation"
            use connection = connect()
            use transaction = connection.BeginTransaction()
            if next.Mode = TradingAuthorityMode.Remote then
                use materialize = connection.CreateCommand()
                materialize.Transaction <- transaction
                materialize.CommandText <- """
DELETE FROM canonical_recommendations;
DELETE FROM canonical_positions;
DELETE FROM canonical_trades;
INSERT INTO canonical_recommendations(identity,source_signal_id,payload_json,status,broker_order_id,version)
 SELECT identity,json_extract(payload_json,'$.sourceSignalId'),payload_json,'Imported',json_extract(payload_json,'$.entryOrderId'),1
 FROM projections WHERE kind='recommendation';
INSERT INTO canonical_positions(identity,source_signal_id,payload_json,execution_context_json,version)
 SELECT identity,json_extract(payload_json,'$.sourceSignalId'),payload_json,'{}',1
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

    member this.AcceptEntry(intent: TradingEntryIntent) =
        let authority = this.Authority()
        use connection = connect()
        let accountGeneration = Int64.Parse(value connection "account_generation")
        match Option.ofObj (TradingCoreCompatibilityPolicy.Error(
            intent, authority, accountGeneration, DateTime.UtcNow)) with
        | Some error -> invalidArg "intent" error
        | None ->
            use transaction = connection.BeginTransaction()
            use existing = connection.CreateCommand()
            existing.Transaction <- transaction
            existing.CommandText <- "SELECT payload_hash,receipt_json FROM inbox WHERE command_id=$id"
            existing.Parameters.AddWithValue("$id", intent.Envelope.CommandId) |> ignore
            use reader = existing.ExecuteReader()
            if reader.Read() then
                let storedHash, receiptJson = reader.GetString(0), reader.GetString(1)
                reader.Close()
                if storedHash <> intent.Envelope.PayloadHash then invalidOp "command-id-payload-conflict"
                transaction.Rollback()
                let receipt = JsonSerializer.Deserialize<TradingCommandReceipt>(receiptJson, json)
                if isNull receipt then invalidOp "empty-stored-command-receipt"
                TradingCommandReceipt(receipt.ContractVersion, receipt.CommandId,
                    receipt.PayloadHash, receipt.Status, receipt.FinancialIdentity,
                    receipt.Message, receipt.AcceptedAtUtc, true)
            else
                reader.Close()
                let acceptedAt = DateTime.UtcNow
                let receipt = TradingCommandReceipt(
                    TradingCoreContractVersions.Current, intent.Envelope.CommandId,
                    intent.Envelope.PayloadHash, TradingCommandStatuses.PendingBrokerSubmission,
                    intent.SourceSignalId, "accepted-for-durable-processing", acceptedAt, false)
                let payloadJson = JsonSerializer.Serialize(intent, json)
                use insertIntent = connection.CreateCommand()
                insertIntent.Transaction <- transaction
                insertIntent.CommandText <- "INSERT INTO financial_intents VALUES($id,$kind,$hash,$payload,$status,NULL,$at,$at)"
                insertIntent.Parameters.AddWithValue("$id", intent.Envelope.CommandId) |> ignore
                insertIntent.Parameters.AddWithValue("$kind", intent.Envelope.CommandKind) |> ignore
                insertIntent.Parameters.AddWithValue("$hash", intent.Envelope.PayloadHash) |> ignore
                insertIntent.Parameters.AddWithValue("$payload", payloadJson) |> ignore
                insertIntent.Parameters.AddWithValue("$status", receipt.Status) |> ignore
                insertIntent.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                insertIntent.ExecuteNonQuery() |> ignore
                let recommendation = TradingRecommendationProjection(
                    intent.Envelope.CommandId, intent.SourceSignalId, intent.Symbol,
                    intent.PatternCode, intent.CustomPatternName, intent.Envelope.OccurredAtUtc,
                    intent.EntryPrice, intent.StopLossPrice, intent.TargetPrice,
                    intent.ShareQuantity, intent.Expectancy, "AutoOrder", false,
                    Nullable(), intent.AccountId, null, null)
                use insertRecommendation = connection.CreateCommand()
                insertRecommendation.Transaction <- transaction
                insertRecommendation.CommandText <- "INSERT INTO canonical_recommendations VALUES($id,$signal,$payload,$status,NULL,1)"
                insertRecommendation.Parameters.AddWithValue("$id", intent.Envelope.CommandId) |> ignore
                insertRecommendation.Parameters.AddWithValue("$signal", intent.SourceSignalId) |> ignore
                insertRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(recommendation, json)) |> ignore
                insertRecommendation.Parameters.AddWithValue("$status", receipt.Status) |> ignore
                insertRecommendation.ExecuteNonQuery() |> ignore
                use insertInbox = connection.CreateCommand()
                insertInbox.Transaction <- transaction
                insertInbox.CommandText <- "INSERT INTO inbox VALUES($id,$kind,$hash,$receipt,$at)"
                insertInbox.Parameters.AddWithValue("$id", intent.Envelope.CommandId) |> ignore
                insertInbox.Parameters.AddWithValue("$kind", intent.Envelope.CommandKind) |> ignore
                insertInbox.Parameters.AddWithValue("$hash", intent.Envelope.PayloadHash) |> ignore
                insertInbox.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, json)) |> ignore
                insertInbox.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                insertInbox.ExecuteNonQuery() |> ignore
                let eventId = intent.Envelope.CommandId + ":accepted"
                let eventPayload = JsonSerializer.Serialize(
                    {| commandId = intent.Envelope.CommandId; sourceSignalId = intent.SourceSignalId
                       symbol = intent.Symbol; status = receipt.Status |}, json)
                use outbox = connection.CreateCommand()
                outbox.Transaction <- transaction
                outbox.CommandText <- "INSERT INTO outbox VALUES($event,$aggregate,1,$payload,$at,NULL)"
                outbox.Parameters.AddWithValue("$event", eventId) |> ignore
                outbox.Parameters.AddWithValue("$aggregate", intent.SourceSignalId) |> ignore
                outbox.Parameters.AddWithValue("$payload", eventPayload) |> ignore
                outbox.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                outbox.ExecuteNonQuery() |> ignore
                transaction.Commit()
                receipt

    member _.AccountConfiguration() =
        use connection = connect()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT ciphertext,nonce,tag FROM account_configuration WHERE singleton=1"
        use reader = command.ExecuteReader()
        if not (reader.Read()) then None
        else
            let payload =
                { Ciphertext = reader.GetFieldValue<byte array>(0)
                  Nonce = reader.GetFieldValue<byte array>(1)
                  Tag = reader.GetFieldValue<byte array>(2) }
            match Option.ofObj (JsonSerializer.Deserialize<TradingAccountConfigurationSet>(
                secrets.Unprotect payload, json)) with
            | Some configuration -> Some configuration
            | None -> invalidOp "empty-stored-account-configuration"

    member this.ClaimEntry() =
        let authority = this.Authority()
        if authority.Mode <> TradingAuthorityMode.Remote then None
        else
            use connection = connect()
            use transaction = connection.BeginTransaction()
            use select = connection.CreateCommand()
            select.Transaction <- transaction
            select.CommandText <- "SELECT command_id,payload_json FROM financial_intents WHERE command_kind=$kind AND status=$status ORDER BY accepted_at LIMIT 1"
            select.Parameters.AddWithValue("$kind", TradingCommandKinds.AcceptEntry) |> ignore
            select.Parameters.AddWithValue("$status", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            use reader = select.ExecuteReader()
            if not (reader.Read()) then None
            else
                let commandId, payload = reader.GetString(0), reader.GetString(1)
                reader.Close()
                use update = connection.CreateCommand()
                update.Transaction <- transaction
                update.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$pending"
                update.Parameters.AddWithValue("$status", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
                update.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
                update.Parameters.AddWithValue("$id", commandId) |> ignore
                update.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
                if update.ExecuteNonQuery() <> 1 then None
                else
                    transaction.Commit()
                    match Option.ofObj (JsonSerializer.Deserialize<TradingEntryIntent>(payload, json)) with
                    | Some intent -> Some intent
                    | None -> invalidOp "empty-stored-entry-intent"

    member this.UnresolvedEntry() =
        if this.Authority().Mode <> TradingAuthorityMode.Remote then None
        else
            use connection = connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM financial_intents WHERE command_kind=$kind AND status IN ($awaiting,$reconcile) ORDER BY accepted_at LIMIT 1"
            command.Parameters.AddWithValue("$kind", TradingCommandKinds.AcceptEntry) |> ignore
            command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            match command.ExecuteScalar() with
            | null -> None
            | payload ->
                match Option.ofObj (JsonSerializer.Deserialize<TradingEntryIntent>(Convert.ToString payload, json)) with
                | Some intent -> Some intent
                | None -> invalidOp "empty-stored-unresolved-entry"

    member _.RecordBrokerEvidence(commandId: string, evidence: BrokerOrderEvidence) =
        use connection = connect()
        use transaction = connection.BeginTransaction()
        use load = connection.CreateCommand()
        load.Transaction <- transaction
        load.CommandText <- "SELECT payload_json FROM financial_intents WHERE command_id=$id AND status IN ($pending,$reconcile)"
        load.Parameters.AddWithValue("$id", commandId) |> ignore
        load.Parameters.AddWithValue("$pending", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
        load.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
        match load.ExecuteScalar() with
        | null -> transaction.Rollback(); false
        | payload ->
            let intent = JsonSerializer.Deserialize<TradingEntryIntent>(Convert.ToString payload, json)
            if isNull intent then invalidOp "empty-entry-intent-for-broker-evidence"
            let observedAt = DateTime.UtcNow
            use brokerEvidence = connection.CreateCommand()
            brokerEvidence.Transaction <- transaction
            brokerEvidence.CommandText <- """INSERT INTO broker_evidence VALUES($order,$client,$command,$payload,$at)
ON CONFLICT(order_id) DO UPDATE SET payload_json=excluded.payload_json,observed_at=excluded.observed_at
WHERE broker_evidence.client_order_id=excluded.client_order_id AND broker_evidence.command_id=excluded.command_id"""
            brokerEvidence.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
            brokerEvidence.Parameters.AddWithValue("$client", evidence.ClientOrderId) |> ignore
            brokerEvidence.Parameters.AddWithValue("$command", commandId) |> ignore
            brokerEvidence.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(evidence, json)) |> ignore
            brokerEvidence.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
            if brokerEvidence.ExecuteNonQuery() <> 1 then invalidOp "broker-evidence-identity-conflict"
            let mutable status =
                match evidence.Status with
                | "Rejected" | "Cancelled" | "Expired" -> TradingCommandStatuses.Rejected
                | _ -> TradingCommandStatuses.AwaitingBrokerEvidence
            if evidence.Status = "Filled" then
                try
                    let position = TradingEntrySettlementPolicy.CreateFilledPosition(intent, evidence)
                    let context = JsonSerializer.Serialize(
                        {| strategy = intent.Strategy; marketDataEvidence = intent.MarketDataEvidence |}, json)
                    use insertPosition = connection.CreateCommand()
                    insertPosition.Transaction <- transaction
                    insertPosition.CommandText <- "INSERT OR IGNORE INTO canonical_positions VALUES($id,$signal,$payload,$context,1)"
                    insertPosition.Parameters.AddWithValue("$id", position.PositionId) |> ignore
                    insertPosition.Parameters.AddWithValue("$signal", intent.SourceSignalId) |> ignore
                    insertPosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(position, json)) |> ignore
                    insertPosition.Parameters.AddWithValue("$context", context) |> ignore
                    if insertPosition.ExecuteNonQuery() <> 1 then invalidOp "entry-position-identity-conflict"
                    use loadRecommendation = connection.CreateCommand()
                    loadRecommendation.Transaction <- transaction
                    loadRecommendation.CommandText <- "SELECT payload_json FROM canonical_recommendations WHERE identity=$id"
                    loadRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                    let recommendation = JsonSerializer.Deserialize<TradingRecommendationProjection>(
                        Convert.ToString(loadRecommendation.ExecuteScalar()), json)
                    if isNull recommendation then invalidOp "entry-recommendation-missing"
                    let executed = TradingEntrySettlementPolicy.MarkExecuted(recommendation, evidence)
                    use updateRecommendation = connection.CreateCommand()
                    updateRecommendation.Transaction <- transaction
                    updateRecommendation.CommandText <- "UPDATE canonical_recommendations SET payload_json=$payload,status=$status,broker_order_id=$order,version=version+1 WHERE identity=$id"
                    updateRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(executed, json)) |> ignore
                    updateRecommendation.Parameters.AddWithValue("$status", TradingCommandStatuses.Completed) |> ignore
                    updateRecommendation.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
                    updateRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                    if updateRecommendation.ExecuteNonQuery() <> 1 then invalidOp "entry-recommendation-update-conflict"
                    status <- TradingCommandStatuses.Completed
                with :? ArgumentException -> status <- TradingCommandStatuses.ReconciliationRequired
            use update = connection.CreateCommand()
            update.Transaction <- transaction
            update.CommandText <- "UPDATE financial_intents SET status=$status,broker_order_id=$order,updated_at=$at WHERE command_id=$id"
            update.Parameters.AddWithValue("$status", status) |> ignore
            update.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
            update.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
            update.Parameters.AddWithValue("$id", commandId) |> ignore
            if update.ExecuteNonQuery() <> 1 then invalidOp "entry-intent-update-conflict"
            if status = TradingCommandStatuses.Completed then
                use outbox = connection.CreateCommand()
                outbox.Transaction <- transaction
                outbox.CommandText <- "INSERT OR IGNORE INTO outbox VALUES($event,$aggregate,2,$payload,$at,NULL)"
                outbox.Parameters.AddWithValue("$event", commandId + ":filled") |> ignore
                outbox.Parameters.AddWithValue("$aggregate", intent.SourceSignalId) |> ignore
                outbox.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                    {| commandId = commandId; orderId = evidence.OrderId
                       positionId = TradingEntrySettlementPolicy.PositionId(commandId)
                       status = status |}, json)) |> ignore
                outbox.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
                outbox.ExecuteNonQuery() |> ignore
            transaction.Commit()
            true

    member _.RequireReconciliation(commandId: string) =
        use connection = connect()
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$pending"
        command.Parameters.AddWithValue("$status", TradingCommandStatuses.ReconciliationRequired) |> ignore
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
        command.Parameters.AddWithValue("$id", commandId) |> ignore
        command.Parameters.AddWithValue("$pending", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
        command.ExecuteNonQuery() |> ignore

    member _.RejectIntent(commandId: string, reason: string) =
        use connection = connect()
        use command = connection.CreateCommand()
        command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status IN ($pending,$awaiting,$reconcile)"
        command.Parameters.AddWithValue("$status", TradingCommandStatuses.Rejected) |> ignore
        command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
        command.Parameters.AddWithValue("$id", commandId) |> ignore
        command.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
        command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
        command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
        if command.ExecuteNonQuery() = 1 then
            use audit = connection.CreateCommand()
            audit.CommandText <- "INSERT OR IGNORE INTO outbox VALUES($event,$aggregate,2,$payload,$at,NULL)"
            audit.Parameters.AddWithValue("$event", commandId + ":rejected") |> ignore
            audit.Parameters.AddWithValue("$aggregate", commandId) |> ignore
            audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                {| commandId = commandId; status = TradingCommandStatuses.Rejected; reason = reason |}, json)) |> ignore
            audit.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
            audit.ExecuteNonQuery() |> ignore

    member _.PositionRiskEvidence() =
        use connection = connect()
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT payload_json FROM projections WHERE kind='position'"
        use reader = command.ExecuteReader()
        let positions = ResizeArray<TradingPositionRiskEvidence>()
        while reader.Read() do
            match Option.ofObj (JsonSerializer.Deserialize<TradingPositionProjection>(reader.GetString 0, json)) with
            | Some position when not position.ClosedAtUtc.HasValue ->
                positions.Add(TradingPositionRiskEvidence(position.Symbol, position.Sector))
            | _ -> ()
        positions.ToArray() :> IReadOnlyList<TradingPositionRiskEvidence>

    member this.Status() =
        use connection = connect()
        let count sql =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            Convert.ToInt64(command.ExecuteScalar())
        let authority = this.Authority()
        TradingCoreStatus(TradingCoreContractVersions.Current, true, authority.Mode,
            authority.Generation, Int64.Parse(value connection "account_generation"),
            count "SELECT COUNT(*) FROM inbox", count "SELECT COUNT(*) FROM outbox WHERE delivered_at IS NULL",
            value connection "last_snapshot_id", Nullable(), null)
