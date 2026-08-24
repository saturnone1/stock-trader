namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreEntryStore =
    type TradingCoreStore with
        member this.AcceptEntry(intent: TradingEntryIntent) =
            let authority = this.Authority()
            use connection = this.Connect()
            let accountGeneration = Int64.Parse(this.StateValue(connection, "account_generation"))
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
                    let receipt = JsonSerializer.Deserialize<TradingCommandReceipt>(receiptJson, this.Json)
                    if isNull receipt then invalidOp "empty-stored-command-receipt"
                    use currentStatus = connection.CreateCommand()
                    currentStatus.CommandText <- "SELECT status,broker_order_id FROM financial_intents WHERE command_id=$id"
                    currentStatus.Parameters.AddWithValue("$id", intent.Envelope.CommandId) |> ignore
                    use statusReader = currentStatus.ExecuteReader()
                    if not (statusReader.Read()) then invalidOp "missing-stored-financial-intent"
                    let status = statusReader.GetString 0
                    let financialIdentity =
                        if statusReader.IsDBNull 1 then receipt.FinancialIdentity
                        else statusReader.GetString 1
                    TradingCommandReceipt(receipt.ContractVersion, receipt.CommandId,
                        receipt.PayloadHash, status, financialIdentity,
                        receipt.Message, receipt.AcceptedAtUtc, true)
                else
                    reader.Close()
                    let acceptedAt = DateTime.UtcNow
                    let receipt = TradingCommandReceipt(
                        TradingCoreContractVersions.Current, intent.Envelope.CommandId,
                        intent.Envelope.PayloadHash, TradingCommandStatuses.PendingBrokerSubmission,
                        intent.SourceSignalId, "accepted-for-durable-processing", acceptedAt, false)
                    let payloadJson = JsonSerializer.Serialize(intent, this.Json)
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
                    insertRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(recommendation, this.Json)) |> ignore
                    insertRecommendation.Parameters.AddWithValue("$status", receipt.Status) |> ignore
                    insertRecommendation.ExecuteNonQuery() |> ignore
                    use insertInbox = connection.CreateCommand()
                    insertInbox.Transaction <- transaction
                    insertInbox.CommandText <- "INSERT INTO inbox VALUES($id,$kind,$hash,$receipt,$at)"
                    insertInbox.Parameters.AddWithValue("$id", intent.Envelope.CommandId) |> ignore
                    insertInbox.Parameters.AddWithValue("$kind", intent.Envelope.CommandKind) |> ignore
                    insertInbox.Parameters.AddWithValue("$hash", intent.Envelope.PayloadHash) |> ignore
                    insertInbox.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    insertInbox.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                    insertInbox.ExecuteNonQuery() |> ignore
                    let eventId = intent.Envelope.CommandId + ":accepted"
                    let eventPayload = JsonSerializer.Serialize(
                        {| commandId = intent.Envelope.CommandId; sourceSignalId = intent.SourceSignalId
                           symbol = intent.Symbol; status = receipt.Status |}, this.Json)
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
