namespace StockTrader.TradingCoreService

open System
open System.Text.Json
open StockTrader.ServiceContracts.TradingCore

[<AutoOpen>]
module TradingCoreRecommendationStore =
    type TradingCoreStore with
        member this.RecordRecommendation(observation: TradingRecommendationObservation) =
            let authority = this.Authority()
            use connection = this.Connect()
            let accountGeneration = Int64.Parse(this.StateValue(connection, "account_generation"))
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error(
                observation, authority, accountGeneration, DateTime.UtcNow)) with
            | Some error -> invalidArg "observation" error
            | None ->
                use transaction = connection.BeginTransaction()
                use existing = connection.CreateCommand()
                existing.Transaction <- transaction
                existing.CommandText <- "SELECT payload_hash,receipt_json FROM inbox WHERE command_id=$id"
                existing.Parameters.AddWithValue("$id", observation.Envelope.CommandId) |> ignore
                use reader = existing.ExecuteReader()
                if reader.Read() then
                    let storedHash, receiptJson = reader.GetString 0, reader.GetString 1
                    reader.Close()
                    if storedHash <> observation.Envelope.PayloadHash then
                        invalidOp "command-id-payload-conflict"
                    transaction.Rollback()
                    let receipt = JsonSerializer.Deserialize<TradingCommandReceipt>(receiptJson, this.Json)
                    if isNull receipt then invalidOp "empty-recommendation-receipt"
                    TradingCommandReceipt(
                        receipt.ContractVersion, receipt.CommandId, receipt.PayloadHash,
                        receipt.Status, receipt.FinancialIdentity, receipt.Message,
                        receipt.AcceptedAtUtc, true)
                else
                    reader.Close()
                    let acceptedAt = DateTime.UtcNow
                    let receipt = TradingCommandReceipt(
                        TradingCoreContractVersions.Current, observation.Envelope.CommandId,
                        observation.Envelope.PayloadHash, TradingCommandStatuses.Completed,
                        observation.SourceSignalId, "recommendation-recorded", acceptedAt, false)
                    let recommendation = TradingRecommendationProjection(
                        observation.Envelope.CommandId, observation.SourceSignalId,
                        observation.Symbol, observation.PatternCode, observation.CustomPatternName,
                        observation.Envelope.OccurredAtUtc, observation.EntryPrice,
                        observation.StopLossPrice, observation.TargetPrice,
                        observation.ShareQuantity, observation.Expectancy, "AlertOnly", false,
                        Nullable(), null, null, "alert-only")
                    use insertRecommendation = connection.CreateCommand()
                    insertRecommendation.Transaction <- transaction
                    insertRecommendation.CommandText <- "INSERT INTO canonical_recommendations VALUES($id,$signal,$payload,$status,NULL,1)"
                    insertRecommendation.Parameters.AddWithValue("$id", observation.Envelope.CommandId) |> ignore
                    insertRecommendation.Parameters.AddWithValue("$signal", observation.SourceSignalId) |> ignore
                    insertRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(recommendation, this.Json)) |> ignore
                    insertRecommendation.Parameters.AddWithValue("$status", TradingCommandStatuses.Completed) |> ignore
                    insertRecommendation.ExecuteNonQuery() |> ignore
                    use inbox = connection.CreateCommand()
                    inbox.Transaction <- transaction
                    inbox.CommandText <- "INSERT INTO inbox VALUES($id,$kind,$hash,$receipt,$at)"
                    inbox.Parameters.AddWithValue("$id", observation.Envelope.CommandId) |> ignore
                    inbox.Parameters.AddWithValue("$kind", observation.Envelope.CommandKind) |> ignore
                    inbox.Parameters.AddWithValue("$hash", observation.Envelope.PayloadHash) |> ignore
                    inbox.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    inbox.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                    inbox.ExecuteNonQuery() |> ignore
                    use audit = connection.CreateCommand()
                    audit.Transaction <- transaction
                    audit.CommandText <- "INSERT INTO outbox VALUES($event,$aggregate,1,$payload,$at,NULL)"
                    audit.Parameters.AddWithValue("$event", observation.Envelope.CommandId + ":recorded") |> ignore
                    audit.Parameters.AddWithValue("$aggregate", observation.SourceSignalId) |> ignore
                    audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                        {| commandId = observation.Envelope.CommandId
                           sourceSignalId = observation.SourceSignalId
                           status = TradingCommandStatuses.Completed |}, this.Json)) |> ignore
                    audit.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                    audit.ExecuteNonQuery() |> ignore
                    transaction.Commit()
                    receipt
