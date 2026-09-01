namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreEntryBrokerStore =
    type TradingCoreStore with
        member this.AccountConfiguration() =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT ciphertext,nonce,tag,encryption_key_generation FROM account_configuration WHERE singleton=1"
            use reader = command.ExecuteReader()
            if not (reader.Read()) then None
            else
                let payload =
                    { Ciphertext = reader.GetFieldValue<byte array>(0)
                      Nonce = reader.GetFieldValue<byte array>(1)
                      Tag = reader.GetFieldValue<byte array>(2) }
                match Option.ofObj (JsonSerializer.Deserialize<TradingAccountConfigurationSet>(
                    this.Secrets.Unprotect(payload, reader.GetString(3)), this.Json)) with
                | Some configuration -> Some configuration
                | None -> invalidOp "empty-stored-account-configuration"
    
        member this.ClaimEntry() =
            let authority = this.Authority()
            if authority.Mode <> TradingAuthorityMode.Remote then None
            else
                use connection = this.Connect()
                use transaction = connection.BeginTransaction()
                use select = connection.CreateCommand()
                select.Transaction <- transaction
                select.CommandText <- "SELECT command_id,payload_json FROM financial_intents WHERE command_kind=$kind AND status=$status AND julianday(json_extract(payload_json,'$.envelope.expiresAtUtc')) > julianday($observed) ORDER BY accepted_at LIMIT 1"
                select.Parameters.AddWithValue("$kind", TradingCommandKinds.AcceptEntry) |> ignore
                select.Parameters.AddWithValue("$status", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
                select.Parameters.AddWithValue("$observed", this.UtcNow.ToString("O")) |> ignore
                use reader = select.ExecuteReader()
                if not (reader.Read()) then None
                else
                    let commandId, payload = reader.GetString(0), reader.GetString(1)
                    reader.Close()
                    use update = connection.CreateCommand()
                    update.Transaction <- transaction
                    update.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$pending"
                    update.Parameters.AddWithValue("$status", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
                    update.Parameters.AddWithValue("$at", this.UtcNow.ToString("O")) |> ignore
                    update.Parameters.AddWithValue("$id", commandId) |> ignore
                    update.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
                    if update.ExecuteNonQuery() <> 1 then None
                    else
                        transaction.Commit()
                        match Option.ofObj (JsonSerializer.Deserialize<TradingEntryIntent>(payload, this.Json)) with
                        | Some intent -> Some intent
                        | None -> invalidOp "empty-stored-entry-intent"
    
        member this.UnresolvedEntry() =
            if this.Authority().Mode <> TradingAuthorityMode.Remote then None
            else
                use connection = this.Connect()
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT payload_json FROM financial_intents WHERE command_kind=$kind AND status IN ($awaiting,$reconcile) ORDER BY accepted_at LIMIT 1"
                command.Parameters.AddWithValue("$kind", TradingCommandKinds.AcceptEntry) |> ignore
                command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
                command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
                match command.ExecuteScalar() with
                | null -> None
                | payload ->
                    match Option.ofObj (JsonSerializer.Deserialize<TradingEntryIntent>(Convert.ToString payload, this.Json)) with
                    | Some intent -> Some intent
                    | None -> invalidOp "empty-stored-unresolved-entry"
    
        member this.RecordBrokerEvidence(commandId: string, evidence: BrokerOrderEvidence) =
            use connection = this.Connect()
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
                let intent = JsonSerializer.Deserialize<TradingEntryIntent>(Convert.ToString payload, this.Json)
                if isNull intent then invalidOp "empty-entry-intent-for-broker-evidence"
                let observedAt = this.UtcNow
                use brokerEvidence = connection.CreateCommand()
                brokerEvidence.Transaction <- transaction
                brokerEvidence.CommandText <- """INSERT INTO broker_evidence VALUES($order,$client,$command,$payload,$at)
    ON CONFLICT(order_id) DO UPDATE SET payload_json=excluded.payload_json,observed_at=excluded.observed_at
    WHERE broker_evidence.client_order_id=excluded.client_order_id AND broker_evidence.command_id=excluded.command_id"""
                brokerEvidence.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
                brokerEvidence.Parameters.AddWithValue("$client", evidence.ClientOrderId) |> ignore
                brokerEvidence.Parameters.AddWithValue("$command", commandId) |> ignore
                brokerEvidence.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(evidence, this.Json)) |> ignore
                brokerEvidence.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
                if brokerEvidence.ExecuteNonQuery() <> 1 then invalidOp "broker-evidence-identity-conflict"
                let mutable status =
                    match evidence.Status with
                    | "Rejected" | "Cancelled" | "Expired" -> TradingCommandStatuses.Rejected
                    | _ -> TradingCommandStatuses.AwaitingBrokerEvidence
                if evidence.Status = "Filled"
                    || (status = TradingCommandStatuses.Rejected && evidence.FilledQuantity > 0) then
                    try
                        let position = TradingEntrySettlementPolicy.CreateTerminalPosition(
                            intent, evidence, observedAt)
                        let context = JsonSerializer.Serialize(position.ExecutionContext, this.Json)
                        use insertPosition = connection.CreateCommand()
                        insertPosition.Transaction <- transaction
                        insertPosition.CommandText <- "INSERT OR IGNORE INTO canonical_positions VALUES($id,$signal,$payload,$context,1)"
                        insertPosition.Parameters.AddWithValue("$id", position.PositionId) |> ignore
                        insertPosition.Parameters.AddWithValue("$signal", intent.SourceSignalId) |> ignore
                        insertPosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(position, this.Json)) |> ignore
                        insertPosition.Parameters.AddWithValue("$context", context) |> ignore
                        if insertPosition.ExecuteNonQuery() = 0 then
                            use existingPosition = connection.CreateCommand()
                            existingPosition.Transaction <- transaction
                            existingPosition.CommandText <- "SELECT source_signal_id FROM canonical_positions WHERE identity=$id"
                            existingPosition.Parameters.AddWithValue("$id", position.PositionId) |> ignore
                            if Convert.ToString(existingPosition.ExecuteScalar()) <> intent.SourceSignalId then
                                invalidOp "entry-position-identity-conflict"
                        use loadRecommendation = connection.CreateCommand()
                        loadRecommendation.Transaction <- transaction
                        loadRecommendation.CommandText <- "SELECT payload_json FROM canonical_recommendations WHERE identity=$id"
                        loadRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                        let recommendation = JsonSerializer.Deserialize<TradingRecommendationProjection>(
                            Convert.ToString(loadRecommendation.ExecuteScalar()), this.Json)
                        if isNull recommendation then invalidOp "entry-recommendation-missing"
                        let executed = TradingEntrySettlementPolicy.MarkExecuted(recommendation, evidence)
                        use updateRecommendation = connection.CreateCommand()
                        updateRecommendation.Transaction <- transaction
                        updateRecommendation.CommandText <- "UPDATE canonical_recommendations SET payload_json=$payload,status=$status,broker_order_id=$order,version=version+1 WHERE identity=$id"
                        updateRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(executed, this.Json)) |> ignore
                        updateRecommendation.Parameters.AddWithValue("$status", TradingCommandStatuses.Completed) |> ignore
                        updateRecommendation.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
                        updateRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                        if updateRecommendation.ExecuteNonQuery() <> 1 then invalidOp "entry-recommendation-update-conflict"
                        status <- TradingCommandStatuses.Completed
                    with :? ArgumentException -> status <- TradingCommandStatuses.ReconciliationRequired
                if status = TradingCommandStatuses.Rejected then
                    use loadRecommendation = connection.CreateCommand()
                    loadRecommendation.Transaction <- transaction
                    loadRecommendation.CommandText <- "SELECT payload_json FROM canonical_recommendations WHERE identity=$id"
                    loadRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                    let recommendation = JsonSerializer.Deserialize<TradingRecommendationProjection>(
                        Convert.ToString(loadRecommendation.ExecuteScalar()), this.Json)
                    if isNull recommendation then invalidOp "entry-recommendation-missing"
                    let rejected = TradingEntrySettlementPolicy.MarkRejected(recommendation, evidence)
                    use updateRecommendation = connection.CreateCommand()
                    updateRecommendation.Transaction <- transaction
                    updateRecommendation.CommandText <- "UPDATE canonical_recommendations SET payload_json=$payload,status=$status,broker_order_id=$order,version=version+1 WHERE identity=$id"
                    updateRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(rejected, this.Json)) |> ignore
                    updateRecommendation.Parameters.AddWithValue("$status", status) |> ignore
                    updateRecommendation.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
                    updateRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                    if updateRecommendation.ExecuteNonQuery() <> 1 then invalidOp "entry-recommendation-rejection-conflict"
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
                           status = status |}, this.Json)) |> ignore
                    outbox.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
                    outbox.ExecuteNonQuery() |> ignore
                transaction.Commit()
                true
