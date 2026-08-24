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
        member this.RequireReconciliation(commandId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$pending"
            command.Parameters.AddWithValue("$status", TradingCommandStatuses.ReconciliationRequired) |> ignore
            command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
            command.Parameters.AddWithValue("$id", commandId) |> ignore
            command.Parameters.AddWithValue("$pending", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.ExecuteNonQuery() |> ignore
    
        member this.ReleaseEntryForRetry(commandId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "UPDATE financial_intents SET status=$status,updated_at=$at WHERE command_id=$id AND status=$awaiting"
            command.Parameters.AddWithValue("$status", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
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
            command.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
            command.Parameters.AddWithValue("$id", commandId) |> ignore
            command.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            if command.ExecuteNonQuery() = 1 then
                use audit = connection.CreateCommand()
                audit.Transaction <- transaction
                audit.CommandText <- "INSERT OR IGNORE INTO outbox VALUES($event,$aggregate,2,$payload,$at,NULL)"
                audit.Parameters.AddWithValue("$event", commandId + ":rejected") |> ignore
                audit.Parameters.AddWithValue("$aggregate", commandId) |> ignore
                audit.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(
                    {| commandId = commandId; status = TradingCommandStatuses.Rejected; reason = reason |}, this.Json)) |> ignore
                audit.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
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
