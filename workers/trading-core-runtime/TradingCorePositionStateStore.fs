namespace StockTrader.TradingCoreService

open System
open System.Text.Json
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCorePositionStateStore =
    type TradingCoreStore with
        member this.ApplyPositionState(update: TradingPositionPolicyStateUpdate) =
            let authority = this.Authority()
            use connection = this.Connect()
            let accountGeneration = Int64.Parse(this.StateValue(connection, "account_generation"))
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error(
                update, authority, accountGeneration, this.UtcNow)) with
            | Some error -> invalidArg "update" error
            | None ->
                use transaction = connection.BeginTransaction()
                use existing = connection.CreateCommand()
                existing.Transaction <- transaction
                existing.CommandText <- "SELECT payload_hash,receipt_json FROM inbox WHERE command_id=$id"
                existing.Parameters.AddWithValue("$id", update.Envelope.CommandId) |> ignore
                use existingReader = existing.ExecuteReader()
                if existingReader.Read() then
                    let storedHash = existingReader.GetString 0
                    let receiptJson = existingReader.GetString 1
                    existingReader.Close()
                    if storedHash <> update.Envelope.PayloadHash then
                        invalidOp "command-id-payload-conflict"
                    transaction.Rollback()
                    let receipt = JsonSerializer.Deserialize<TradingCommandReceipt>(receiptJson, this.Json)
                    if isNull receipt then invalidOp "empty-position-state-receipt"
                    TradingCommandReceipt(
                        receipt.ContractVersion, receipt.CommandId, receipt.PayloadHash,
                        receipt.Status, receipt.FinancialIdentity, receipt.Message,
                        receipt.AcceptedAtUtc, true)
                else
                    existingReader.Close()
                    use load = connection.CreateCommand()
                    load.Transaction <- transaction
                    load.CommandText <- "SELECT payload_json FROM canonical_positions WHERE identity=$id"
                    load.Parameters.AddWithValue("$id", update.PositionId) |> ignore
                    match load.ExecuteScalar() with
                    | null -> invalidOp "open-position-not-found"
                    | payload ->
                        let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                            Convert.ToString payload, this.Json)
                        if isNull position then invalidOp "empty-position-policy-state"
                        let next = TradingPositionPolicyStateUpdatePolicy.Apply(position, update)
                        let acceptedAt = this.UtcNow
                        let receipt = TradingCommandReceipt(
                            TradingCoreContractVersions.Current, update.Envelope.CommandId,
                            update.Envelope.PayloadHash, TradingCommandStatuses.Completed,
                            update.PositionId, "position-policy-state-updated", acceptedAt, false)
                        use updatePosition = connection.CreateCommand()
                        updatePosition.Transaction <- transaction
                        updatePosition.CommandText <- "UPDATE canonical_positions SET payload_json=$payload,version=version+1 WHERE identity=$id"
                        updatePosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(next, this.Json)) |> ignore
                        updatePosition.Parameters.AddWithValue("$id", update.PositionId) |> ignore
                        if updatePosition.ExecuteNonQuery() <> 1 then
                            invalidOp "position-policy-state-conflict"
                        use inbox = connection.CreateCommand()
                        inbox.Transaction <- transaction
                        inbox.CommandText <- "INSERT INTO inbox VALUES($id,$kind,$hash,$receipt,$at)"
                        inbox.Parameters.AddWithValue("$id", update.Envelope.CommandId) |> ignore
                        inbox.Parameters.AddWithValue("$kind", update.Envelope.CommandKind) |> ignore
                        inbox.Parameters.AddWithValue("$hash", update.Envelope.PayloadHash) |> ignore
                        inbox.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                        inbox.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                        inbox.ExecuteNonQuery() |> ignore
                        use audit = connection.CreateCommand()
                        audit.Transaction <- transaction
                        audit.CommandText <- "INSERT INTO outbox VALUES($event,$aggregate,1,$payload,$at,NULL)"
                        audit.Parameters.AddWithValue("$event", update.Envelope.CommandId + ":applied") |> ignore
                        audit.Parameters.AddWithValue("$aggregate", update.PositionId) |> ignore
                        audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                            {| commandId = update.Envelope.CommandId
                               status = TradingCommandStatuses.Completed |}, this.Json)) |> ignore
                        audit.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                        audit.ExecuteNonQuery() |> ignore
                        transaction.Commit()
                        receipt
