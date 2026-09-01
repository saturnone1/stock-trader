namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCorePositionBrokerStore =
    type TradingCoreStore with
        member this.ClaimPosition() =
            if this.Authority().Mode <> TradingAuthorityMode.Remote then None
            else
                use connection = this.Connect()
                use transaction = connection.BeginTransaction()
                use select = connection.CreateCommand()
                select.Transaction <- transaction
                select.CommandText <- "SELECT command_id,payload_json FROM financial_intents WHERE command_kind=$kind AND status=$status AND julianday(json_extract(payload_json,'$.envelope.expiresAtUtc')) > julianday($observed) ORDER BY accepted_at LIMIT 1"
                select.Parameters.AddWithValue("$kind", TradingCommandKinds.ClosePosition) |> ignore
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
                        match Option.ofObj (JsonSerializer.Deserialize<TradingPositionCommand>(payload, this.Json)) with
                        | Some value -> Some value
                        | None -> invalidOp "empty-stored-position-command"
    
        member this.UnresolvedPosition() =
            if this.Authority().Mode <> TradingAuthorityMode.Remote then None
            else
                use connection = this.Connect()
                use command = connection.CreateCommand()
                command.CommandText <- "SELECT payload_json FROM financial_intents WHERE command_kind=$kind AND status IN ($awaiting,$reconcile) ORDER BY accepted_at LIMIT 1"
                command.Parameters.AddWithValue("$kind", TradingCommandKinds.ClosePosition) |> ignore
                command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
                command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
                match command.ExecuteScalar() with
                | null -> None
                | payload -> Option.ofObj (JsonSerializer.Deserialize<TradingPositionCommand>(
                    Convert.ToString payload, this.Json))
    
        member this.LoadPosition(positionId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM canonical_positions WHERE identity=$id"
            command.Parameters.AddWithValue("$id", positionId) |> ignore
            match command.ExecuteScalar() with
            | null -> None
            | payload -> Option.ofObj (JsonSerializer.Deserialize<TradingPositionProjection>(
                Convert.ToString payload, this.Json))
    
        member this.RecordPositionBrokerEvidence(commandId: string, evidence: BrokerOrderEvidence) =
            use connection = this.Connect()
            use transaction = connection.BeginTransaction()
            use load = connection.CreateCommand()
            load.Transaction <- transaction
            load.CommandText <- "SELECT payload_json FROM financial_intents WHERE command_id=$id AND status IN ($awaiting,$reconcile)"
            load.Parameters.AddWithValue("$id", commandId) |> ignore
            load.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            load.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            match load.ExecuteScalar() with
            | null -> transaction.Rollback(); false
            | payload ->
                let command = JsonSerializer.Deserialize<TradingPositionCommand>(Convert.ToString payload, this.Json)
                if isNull command then invalidOp "empty-position-command-for-broker-evidence"
                let observedAt = this.UtcNow
                use saveEvidence = connection.CreateCommand()
                saveEvidence.Transaction <- transaction
                saveEvidence.CommandText <- "INSERT INTO broker_evidence VALUES($order,$client,$command,$payload,$at) ON CONFLICT(order_id) DO UPDATE SET payload_json=excluded.payload_json,observed_at=excluded.observed_at WHERE broker_evidence.client_order_id=excluded.client_order_id AND broker_evidence.command_id=excluded.command_id"
                saveEvidence.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
                saveEvidence.Parameters.AddWithValue("$client", evidence.ClientOrderId) |> ignore
                saveEvidence.Parameters.AddWithValue("$command", commandId) |> ignore
                saveEvidence.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(evidence, this.Json)) |> ignore
                saveEvidence.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
                if saveEvidence.ExecuteNonQuery() <> 1 then invalidOp "broker-evidence-identity-conflict"
                let mutable status =
                    match evidence.Status with
                    | "Rejected" | "Cancelled" | "Expired" -> TradingCommandStatuses.Rejected
                    | _ -> TradingCommandStatuses.AwaitingBrokerEvidence
                if evidence.Status = "Filled"
                    || (status = TradingCommandStatuses.Rejected && evidence.FilledQuantity > 0) then
                    try
                        use loadPosition = connection.CreateCommand()
                        loadPosition.Transaction <- transaction
                        loadPosition.CommandText <- "SELECT payload_json FROM canonical_positions WHERE identity=$id"
                        loadPosition.Parameters.AddWithValue("$id", command.PositionId) |> ignore
                        let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                            Convert.ToString(loadPosition.ExecuteScalar()), this.Json)
                        if isNull position then invalidOp "position-not-found-for-settlement"
                        let settlement = TradingPositionSettlementPolicy.ApplyTerminalOrder(
                            position, command, evidence, observedAt)
                        use updatePosition = connection.CreateCommand()
                        updatePosition.Transaction <- transaction
                        updatePosition.CommandText <- "UPDATE canonical_positions SET payload_json=$payload,version=version+1 WHERE identity=$id"
                        updatePosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settlement.Position, this.Json)) |> ignore
                        updatePosition.Parameters.AddWithValue("$id", command.PositionId) |> ignore
                        if updatePosition.ExecuteNonQuery() <> 1 then invalidOp "position-settlement-conflict"
                        if not (isNull settlement.Trade) then
                            use trade = connection.CreateCommand()
                            trade.Transaction <- transaction
                            trade.CommandText <- "INSERT OR IGNORE INTO canonical_trades VALUES($id,$payload,1)"
                            trade.Parameters.AddWithValue("$id", settlement.Trade.TradeId) |> ignore
                            trade.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settlement.Trade, this.Json)) |> ignore
                            trade.ExecuteNonQuery() |> ignore
                        status <- TradingCommandStatuses.Completed
                    with :? ArgumentException -> status <- TradingCommandStatuses.ReconciliationRequired
                elif status = TradingCommandStatuses.Rejected then
                    use loadPosition = connection.CreateCommand()
                    loadPosition.Transaction <- transaction
                    loadPosition.CommandText <- "SELECT payload_json FROM canonical_positions WHERE identity=$id"
                    loadPosition.Parameters.AddWithValue("$id", command.PositionId) |> ignore
                    let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                        Convert.ToString(loadPosition.ExecuteScalar()), this.Json)
                    if isNull position then invalidOp "position-not-found-for-rejection"
                    let released = TradingPositionCommandStatePolicy.ClearRequest(position)
                    use updatePosition = connection.CreateCommand()
                    updatePosition.Transaction <- transaction
                    updatePosition.CommandText <- "UPDATE canonical_positions SET payload_json=$payload,version=version+1 WHERE identity=$id"
                    updatePosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(released, this.Json)) |> ignore
                    updatePosition.Parameters.AddWithValue("$id", command.PositionId) |> ignore
                    if updatePosition.ExecuteNonQuery() <> 1 then invalidOp "position-rejection-state-conflict"
                use updateIntent = connection.CreateCommand()
                updateIntent.Transaction <- transaction
                updateIntent.CommandText <- "UPDATE financial_intents SET status=$status,broker_order_id=$order,updated_at=$at WHERE command_id=$id"
                updateIntent.Parameters.AddWithValue("$status", status) |> ignore
                updateIntent.Parameters.AddWithValue("$order", evidence.OrderId) |> ignore
                updateIntent.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
                updateIntent.Parameters.AddWithValue("$id", commandId) |> ignore
                if updateIntent.ExecuteNonQuery() <> 1 then invalidOp "position-intent-update-conflict"
                if status = TradingCommandStatuses.Completed then
                    use audit = connection.CreateCommand()
                    audit.Transaction <- transaction
                    audit.CommandText <- "INSERT OR IGNORE INTO outbox VALUES($event,$aggregate,2,$payload,$at,NULL)"
                    audit.Parameters.AddWithValue("$event", commandId + ":filled") |> ignore
                    audit.Parameters.AddWithValue("$aggregate", commandId) |> ignore
                    audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                        {| commandId = commandId; positionId = command.PositionId
                           orderId = evidence.OrderId; status = status |}, this.Json)) |> ignore
                    audit.Parameters.AddWithValue("$at", observedAt.ToString("O")) |> ignore
                    audit.ExecuteNonQuery() |> ignore
                transaction.Commit()
                true
