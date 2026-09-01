namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreCommandStore =
    type TradingCoreStore with
        /// Commands that have not crossed the broker boundary are safe to expire. Commands already
        /// awaiting broker evidence must instead be reconciled because submission may have happened.
        member this.RejectExpiredPendingIntents(observedAtUtc: DateTime) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT command_id FROM financial_intents WHERE status=$pending AND julianday(json_extract(payload_json,'$.envelope.expiresAtUtc')) <= julianday($observed) ORDER BY accepted_at"
            command.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            command.Parameters.AddWithValue("$observed", observedAtUtc.ToUniversalTime().ToString("O")) |> ignore
            use reader = command.ExecuteReader()
            let expired = ResizeArray<string>()
            while reader.Read() do expired.Add(reader.GetString 0)
            reader.Close()
            for commandId in expired do
                this.RejectIntent(commandId, "command-expired-before-broker-submission")
            expired.Count

        member this.RequireReconciliation(commandId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$pending"
            command.Parameters.AddWithValue("$status", TradingCommandStatuses.ReconciliationRequired) |> ignore
            command.Parameters.AddWithValue("$at", this.UtcNow.ToString("O")) |> ignore
            command.Parameters.AddWithValue("$id", commandId) |> ignore
            command.Parameters.AddWithValue("$pending", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.ExecuteNonQuery() |> ignore
    
        member this.ReleaseEntryForRetry(commandId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$awaiting"
            command.Parameters.AddWithValue("$status", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            command.Parameters.AddWithValue("$at", this.UtcNow.ToString("O")) |> ignore
            command.Parameters.AddWithValue("$id", commandId) |> ignore
            command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.ExecuteNonQuery() = 1
    
        member this.RejectIntent(commandId: string, reason: string) =
            use connection = this.Connect()
            use transaction = connection.BeginTransaction()
            use command = connection.CreateCommand()
            command.Transaction <- transaction
            command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status IN ($pending,$awaiting,$reconcile)"
            command.Parameters.AddWithValue("$status", TradingCommandStatuses.Rejected) |> ignore
            command.Parameters.AddWithValue("$at", this.UtcNow.ToString("O")) |> ignore
            command.Parameters.AddWithValue("$id", commandId) |> ignore
            command.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            if command.ExecuteNonQuery() = 1 then
                use loadIntent = connection.CreateCommand()
                loadIntent.Transaction <- transaction
                loadIntent.CommandText <- "SELECT command_kind,payload_json FROM financial_intents WHERE command_id=$id"
                loadIntent.Parameters.AddWithValue("$id", commandId) |> ignore
                use intentReader = loadIntent.ExecuteReader()
                if intentReader.Read() then
                    let kind, payload = intentReader.GetString 0, intentReader.GetString 1
                    intentReader.Close()
                    if kind = TradingCommandKinds.AcceptEntry then
                        let intent = JsonSerializer.Deserialize<TradingEntryIntent>(payload, this.Json)
                        if not (isNull intent) then
                            use loadRecommendation = connection.CreateCommand()
                            loadRecommendation.Transaction <- transaction
                            loadRecommendation.CommandText <- "SELECT payload_json FROM canonical_recommendations WHERE identity=$id"
                            loadRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                            let recommendation = JsonSerializer.Deserialize<TradingRecommendationProjection>(
                                Convert.ToString(loadRecommendation.ExecuteScalar()), this.Json)
                            if not (isNull recommendation) then
                                let rejected = TradingEntrySettlementPolicy.MarkRejected(recommendation, reason)
                                use updateRecommendation = connection.CreateCommand()
                                updateRecommendation.Transaction <- transaction
                                updateRecommendation.CommandText <- "UPDATE canonical_recommendations SET payload_json=$payload,status=$status,version=version+1 WHERE identity=$id"
                                updateRecommendation.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(rejected, this.Json)) |> ignore
                                updateRecommendation.Parameters.AddWithValue("$status", TradingCommandStatuses.Rejected) |> ignore
                                updateRecommendation.Parameters.AddWithValue("$id", commandId) |> ignore
                                updateRecommendation.ExecuteNonQuery() |> ignore
                    elif kind = TradingCommandKinds.ClosePosition then
                        let positionCommand = JsonSerializer.Deserialize<TradingPositionCommand>(payload, this.Json)
                        if not (isNull positionCommand) then
                            use loadPosition = connection.CreateCommand()
                            loadPosition.Transaction <- transaction
                            loadPosition.CommandText <- "SELECT payload_json FROM canonical_positions WHERE identity=$id"
                            loadPosition.Parameters.AddWithValue("$id", positionCommand.PositionId) |> ignore
                            match loadPosition.ExecuteScalar() with
                            | null -> ()
                            | positionPayload ->
                                let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                                    Convert.ToString positionPayload, this.Json)
                                if not (isNull position) then
                                    let released = TradingPositionCommandStatePolicy.ClearRequest(position)
                                    use updatePosition = connection.CreateCommand()
                                    updatePosition.Transaction <- transaction
                                    updatePosition.CommandText <- "UPDATE canonical_positions SET payload_json=$payload,version=version+1 WHERE identity=$id"
                                    updatePosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(released, this.Json)) |> ignore
                                    updatePosition.Parameters.AddWithValue("$id", positionCommand.PositionId) |> ignore
                                    updatePosition.ExecuteNonQuery() |> ignore
                else intentReader.Close()
                use audit = connection.CreateCommand()
                audit.Transaction <- transaction
                audit.CommandText <- "INSERT OR IGNORE INTO outbox VALUES($event,$aggregate,2,$payload,$at,NULL)"
                audit.Parameters.AddWithValue("$event", commandId + ":rejected") |> ignore
                audit.Parameters.AddWithValue("$aggregate", commandId) |> ignore
                audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                    {| commandId = commandId; status = TradingCommandStatuses.Rejected; reason = reason |}, this.Json)) |> ignore
                audit.Parameters.AddWithValue("$at", this.UtcNow.ToString("O")) |> ignore
                audit.ExecuteNonQuery() |> ignore
                transaction.Commit()
            else transaction.Rollback()
    
        member this.CommandStatus(commandId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT command_kind,payload_hash,status,broker_order_id,accepted_at,updated_at FROM financial_intents WHERE command_id=$id"
            command.Parameters.AddWithValue("$id", commandId) |> ignore
            use reader = command.ExecuteReader()
            if not (reader.Read()) then None
            else
                Some (TradingCommandStatusView(
                    TradingCoreContractVersions.Current, commandId, reader.GetString 0,
                    reader.GetString 1, reader.GetString 2,
                    (if reader.IsDBNull 3 then null else reader.GetString 3),
                    DateTime.Parse(reader.GetString 4, null, Globalization.DateTimeStyles.RoundtripKind),
                    DateTime.Parse(reader.GetString 5, null, Globalization.DateTimeStyles.RoundtripKind)))

        member this.LatestPositionCommand(positionId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT command_id FROM financial_intents WHERE json_extract(payload_json,'$.positionId')=$id ORDER BY accepted_at DESC LIMIT 1"
            command.Parameters.AddWithValue("$id", positionId) |> ignore
            match command.ExecuteScalar() with
            | null -> None
            | commandId -> this.CommandStatus(Convert.ToString commandId)

        member this.LatestEntryCommand(sourceSignalId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT command_id FROM financial_intents WHERE json_extract(payload_json,'$.sourceSignalId')=$id ORDER BY accepted_at DESC LIMIT 1"
            command.Parameters.AddWithValue("$id", sourceSignalId) |> ignore
            match command.ExecuteScalar() with
            | null -> None
            | commandId -> this.CommandStatus(Convert.ToString commandId)
