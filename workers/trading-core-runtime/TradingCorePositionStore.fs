namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCorePositionStore =
    type TradingCoreStore with
        member this.AcceptPosition(command: TradingPositionCommand) =
            let authority = this.Authority()
            use connection = this.Connect()
            let accountGeneration = Int64.Parse(this.StateValue(connection, "account_generation"))
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error(
                command, authority, accountGeneration, this.UtcNow)) with
            | Some error -> invalidArg "command" error
            | None ->
                use transaction = connection.BeginTransaction()
                use existing = connection.CreateCommand()
                existing.Transaction <- transaction
                existing.CommandText <- "SELECT payload_hash FROM inbox WHERE command_id=$id"
                existing.Parameters.AddWithValue("$id", command.Envelope.CommandId) |> ignore
                match existing.ExecuteScalar() with
                | stored when not (isNull stored) ->
                    if Convert.ToString(stored) <> command.Envelope.PayloadHash then
                        invalidOp "command-id-payload-conflict"
                    transaction.Rollback()
                    match this.CommandStatus(command.Envelope.CommandId) with
                    | Some status -> TradingCommandReceipt(
                        status.ContractVersion, status.CommandId, status.PayloadHash, status.Status,
                        status.BrokerOrderId, "already-accepted", status.AcceptedAtUtc, true)
                    | None -> invalidOp "missing-stored-position-command"
                | _ ->
                    use loadPosition = connection.CreateCommand()
                    loadPosition.Transaction <- transaction
                    loadPosition.CommandText <- "SELECT payload_json FROM canonical_positions WHERE identity=$id"
                    loadPosition.Parameters.AddWithValue("$id", command.PositionId) |> ignore
                    let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                        Convert.ToString(loadPosition.ExecuteScalar()), this.Json)
                    if isNull position || position.ClosedAtUtc.HasValue then
                        invalidOp "open-position-not-found"
                    if isNull position.ExecutionContext
                        || position.ExecutionContext.ExecutionArtifact.ArtifactId
                            <> command.ExpectedExecutionArtifactId then
                        invalidOp "position-execution-artifact-mismatch"
                    if command.Action <> TradingPositionActionKinds.ScaleIn
                        && command.Quantity > position.Quantity then
                        invalidOp "position-command-quantity-exceeds-open-position"
                    let acceptedAt = this.UtcNow
                    let receipt = TradingCommandReceipt(
                        TradingCoreContractVersions.Current, command.Envelope.CommandId,
                        command.Envelope.PayloadHash, TradingCommandStatuses.PendingBrokerSubmission,
                        command.PositionId, "accepted-for-durable-processing", acceptedAt, false)
                    use insertIntent = connection.CreateCommand()
                    insertIntent.Transaction <- transaction
                    insertIntent.CommandText <- "INSERT INTO financial_intents VALUES($id,$kind,$hash,$payload,$status,NULL,$at,$at)"
                    insertIntent.Parameters.AddWithValue("$id", command.Envelope.CommandId) |> ignore
                    insertIntent.Parameters.AddWithValue("$kind", command.Envelope.CommandKind) |> ignore
                    insertIntent.Parameters.AddWithValue("$hash", command.Envelope.PayloadHash) |> ignore
                    insertIntent.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(command, this.Json)) |> ignore
                    insertIntent.Parameters.AddWithValue("$status", receipt.Status) |> ignore
                    insertIntent.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                    insertIntent.ExecuteNonQuery() |> ignore
                    use insertInbox = connection.CreateCommand()
                    insertInbox.Transaction <- transaction
                    insertInbox.CommandText <- "INSERT INTO inbox VALUES($id,$kind,$hash,$receipt,$at)"
                    insertInbox.Parameters.AddWithValue("$id", command.Envelope.CommandId) |> ignore
                    insertInbox.Parameters.AddWithValue("$kind", command.Envelope.CommandKind) |> ignore
                    insertInbox.Parameters.AddWithValue("$hash", command.Envelope.PayloadHash) |> ignore
                    insertInbox.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    insertInbox.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                    insertInbox.ExecuteNonQuery() |> ignore
                    let evaluated =
                        if isNull command.EvaluatedPolicyState then position
                        else
                            TradingPositionPolicyStateUpdatePolicy.Apply(
                                position,
                                TradingPositionPolicyStateUpdate(
                                    command.Envelope, command.PositionId,
                                    command.ExpectedExecutionArtifactId,
                                    command.EvaluatedPolicyState.HighSinceEntry,
                                    command.EvaluatedPolicyState.StopLossPrice,
                                    command.EvaluatedPolicyState.InitialRiskDistance,
                                    command.EvaluatedPolicyState.BreakevenApplied,
                                    command.EvaluatedPolicyState.TrailingStopActivated,
                                    command.MarketDataEvidence,
                                    command.EvaluatedEntryAtr,
                                    command.EvaluatedThroughBarUtc,
                                    command.EvaluatedMarketDataRevision))
                    let requested = TradingPositionCommandStatePolicy.MarkRequested(evaluated, command)
                    use updatePosition = connection.CreateCommand()
                    updatePosition.Transaction <- transaction
                    updatePosition.CommandText <- "UPDATE canonical_positions SET payload_json=$payload,version=version+1 WHERE identity=$id"
                    updatePosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(requested, this.Json)) |> ignore
                    updatePosition.Parameters.AddWithValue("$id", command.PositionId) |> ignore
                    if updatePosition.ExecuteNonQuery() <> 1 then invalidOp "position-request-state-conflict"
                    use audit = connection.CreateCommand()
                    audit.Transaction <- transaction
                    audit.CommandText <- "INSERT INTO outbox VALUES($event,$aggregate,1,$payload,$at,NULL)"
                    audit.Parameters.AddWithValue("$event", command.Envelope.CommandId + ":accepted") |> ignore
                    audit.Parameters.AddWithValue("$aggregate", command.Envelope.CommandId) |> ignore
                    audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                        {| commandId = command.Envelope.CommandId; action = command.Action
                           status = receipt.Status |}, this.Json)) |> ignore
                    audit.Parameters.AddWithValue("$at", acceptedAt.ToString("O")) |> ignore
                    audit.ExecuteNonQuery() |> ignore
                    transaction.Commit()
                    receipt
