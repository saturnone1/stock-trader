namespace StockTrader.TradingCoreService

open System
open System.Text.Json
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreTransferStore =
    type TradingCoreStore with
        member this.ImportFinancialTransfer(transfer: CanonicalFinancialTransferV2) =
            match Option.ofObj (CanonicalFinancialTransferPolicy.Error transfer) with
            | Some error -> invalidArg "transfer" error
            | None ->
                let transition =
                    match this.Transition transfer.TransitionId with
                    | Some value -> value
                    | None -> invalidOp "authority-transition-not-found"
                if transition.Phase <> AuthorityTransitionPhases.Draining
                    || transition.ReservedGeneration <> transfer.ReservedTargetGeneration
                    || transition.SourceGeneration <> transfer.SourceGeneration
                    || transition.Direction <> transfer.Direction
                    || transition.SourceMode <> transfer.SourceMode then
                    invalidOp "authority-transition-phase-conflict"
                if transfer.Accounts |> Seq.exists (fun account ->
                    account.ConfigurationGeneration <> transition.AccountGeneration) then
                    invalidOp "stale-account-generation"
                let snapshot = CanonicalFinancialTransferMapper.Snapshot transfer
                match Option.ofObj (TradingCoreCompatibilityPolicy.Error snapshot) with
                | Some error -> invalidArg "transfer" error
                | None -> ()
                if snapshot.Positions |> Seq.exists (fun position ->
                    not position.ClosedAtUtc.HasValue
                    && (isNull position.ExecutionContext
                        || isNull position.ExecutionContext.ExecutionArtifact.PositionManagement
                        || not position.LastEvaluatedBarUtc.HasValue)) then
                    invalidOp "open-position-autonomous-protection-incompatible"
                use connection = this.Connect()
                use existing = connection.CreateCommand()
                existing.CommandText <- """SELECT transfer_hash,receipt_json FROM canonical_financial_imports
WHERE transfer_id=$id AND reserved_generation=$generation"""
                existing.Parameters.AddWithValue("$id", transfer.TransferId) |> ignore
                existing.Parameters.AddWithValue("$generation", transfer.ReservedTargetGeneration) |> ignore
                use reader = existing.ExecuteReader()
                if reader.Read() then
                    let storedHash, receiptJson = reader.GetString 0, reader.GetString 1
                    reader.Close()
                    if storedHash <> transfer.TransferHash then invalidOp "transfer-identity-conflict"
                    let receipt = JsonSerializer.Deserialize<CanonicalFinancialImportReceipt>(receiptJson, this.Json)
                    if isNull receipt then invalidOp "empty-financial-import-receipt"
                    CanonicalFinancialImportReceipt(
                        receipt.ContractVersion, receipt.TransferId, receipt.TransferHash,
                        receipt.ReservedGeneration, receipt.ImportStateHash, true,
                        receipt.ImportedAtUtc)
                else
                    reader.Close()
                    use transaction = connection.BeginTransaction()
                    use clear = connection.CreateCommand()
                    clear.Transaction <- transaction
                    clear.CommandText <- """
DELETE FROM canonical_recommendations;
DELETE FROM canonical_positions;
DELETE FROM canonical_trades;
DELETE FROM canonical_risk;
"""
                    clear.ExecuteNonQuery() |> ignore
                    for recommendation in snapshot.Recommendations do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_recommendations VALUES($id,$signal,$payload,$status,$order,1)"
                        command.Parameters.AddWithValue("$id", recommendation.RecommendationId) |> ignore
                        command.Parameters.AddWithValue("$signal", recommendation.SourceSignalId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(recommendation, this.Json)) |> ignore
                        command.Parameters.AddWithValue("$status", "Imported") |> ignore
                        command.Parameters.AddWithValue("$order",
                            if String.IsNullOrWhiteSpace recommendation.EntryOrderId then box DBNull.Value
                            else box recommendation.EntryOrderId) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    for position in snapshot.Positions do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_positions VALUES($id,$signal,$payload,$context,1)"
                        command.Parameters.AddWithValue("$id", position.PositionId) |> ignore
                        command.Parameters.AddWithValue("$signal",
                            if String.IsNullOrWhiteSpace position.SourceSignalId then box DBNull.Value
                            else box position.SourceSignalId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(position, this.Json)) |> ignore
                        command.Parameters.AddWithValue("$context",
                            if isNull position.ExecutionContext then "{}"
                            else JsonSerializer.Serialize(position.ExecutionContext, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    for trade in snapshot.Trades do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_trades VALUES($id,$payload,1)"
                        command.Parameters.AddWithValue("$id", trade.TradeId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(trade, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    use risk = connection.CreateCommand()
                    risk.Transaction <- transaction
                    risk.CommandText <- "INSERT INTO canonical_risk VALUES(1,$payload,1)"
                    risk.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot.Risk, this.Json)) |> ignore
                    risk.ExecuteNonQuery() |> ignore
                    let receipt = CanonicalFinancialImportReceipt(
                        CanonicalFinancialTransferVersions.Current, transfer.TransferId,
                        transfer.TransferHash, transfer.ReservedTargetGeneration,
                        snapshot.SnapshotId, false, transfer.CapturedAtUtc)
                    use record = connection.CreateCommand()
                    record.Transaction <- transaction
                    record.CommandText <- """INSERT INTO canonical_financial_imports
(transfer_id,reserved_generation,transfer_hash,receipt_json,imported_at)
VALUES($id,$generation,$hash,$receipt,$at)"""
                    record.Parameters.AddWithValue("$id", transfer.TransferId) |> ignore
                    record.Parameters.AddWithValue("$generation", transfer.ReservedTargetGeneration) |> ignore
                    record.Parameters.AddWithValue("$hash", transfer.TransferHash) |> ignore
                    record.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    record.Parameters.AddWithValue("$at", transfer.CapturedAtUtc.ToString("O")) |> ignore
                    record.ExecuteNonQuery() |> ignore
                    transaction.Commit()
                    receipt
