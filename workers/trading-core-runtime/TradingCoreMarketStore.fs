namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreMarketStore =
    type TradingCoreStore with
        member this.RefreshRisk(dailyLossLimitPercent: decimal) =
            use connection = this.Connect()
            let accounts = ResizeArray<BrokerAccountEvidence>()
            use accountCommand = connection.CreateCommand()
            accountCommand.CommandText <- "SELECT payload_json FROM broker_accounts ORDER BY account_id"
            use accountReader = accountCommand.ExecuteReader()
            while accountReader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<BrokerAccountEvidence>(
                    accountReader.GetString 0, this.Json)) with
                | Some value -> accounts.Add value
                | None -> invalidOp "empty-broker-account-projection"
            accountReader.Close()
            if accounts.Count > 0 then
                use countCommand = connection.CreateCommand()
                countCommand.CommandText <- "SELECT COUNT(*) FROM canonical_positions WHERE json_extract(payload_json,'$.closedAtUtc') IS NULL"
                let openCount = Convert.ToInt32(countCommand.ExecuteScalar())
                use divergenceCommand = connection.CreateCommand()
                divergenceCommand.CommandText <- "SELECT COUNT(*) FROM state WHERE key LIKE 'portfolio_divergence:%' AND value='true'"
                let hasDivergence = Convert.ToInt64(divergenceCommand.ExecuteScalar()) > 0L
                let risk = TradingPortfolioProjectionPolicy.Risk(
                    accounts.ToArray(), openCount, dailyLossLimitPercent, hasDivergence,
                    DateTime.UtcNow)
                use upsert = connection.CreateCommand()
                upsert.CommandText <- """INSERT INTO canonical_risk(singleton,payload_json,version)
VALUES(1,$payload,1) ON CONFLICT(singleton) DO UPDATE SET
payload_json=excluded.payload_json,version=canonical_risk.version+1"""
                upsert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(risk, this.Json)) |> ignore
                upsert.ExecuteNonQuery() |> ignore

        member this.SyncBrokerPortfolio(
            accountId: string,
            account: BrokerAccountEvidence,
            positions: IReadOnlyList<BrokerPositionEvidence>,
            dailyLossLimitPercent: decimal) =
            use connection = this.Connect()
            use transaction = connection.BeginTransaction()
            use load = connection.CreateCommand()
            load.Transaction <- transaction
            load.CommandText <- "SELECT identity,payload_json FROM canonical_positions WHERE json_extract(payload_json,'$.accountId')=$account AND json_extract(payload_json,'$.closedAtUtc') IS NULL"
            load.Parameters.AddWithValue("$account", accountId) |> ignore
            use reader = load.ExecuteReader()
            let canonical = ResizeArray<string * TradingPositionProjection>()
            while reader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<TradingPositionProjection>(
                    reader.GetString 1, this.Json)) with
                | Some value -> canonical.Add(reader.GetString 0, value)
                | None -> invalidOp "empty-canonical-position-market-payload"
            reader.Close()
            use pendingCommand = connection.CreateCommand()
            pendingCommand.Transaction <- transaction
            pendingCommand.CommandText <- "SELECT COUNT(*) FROM financial_intents WHERE status IN ($pending,$awaiting,$reconcile)"
            pendingCommand.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            pendingCommand.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            pendingCommand.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            let hasPendingIntent = Convert.ToInt64(pendingCommand.ExecuteScalar()) > 0L
            if not hasPendingIntent then
                let canonicalBySymbol =
                    canonical
                    |> Seq.groupBy (fun (_, value) -> value.Symbol.ToUpperInvariant())
                    |> Seq.map (fun (symbol, values) -> symbol, values |> Seq.sumBy (fun (_, value) -> value.Quantity))
                    |> Map.ofSeq
                let brokerBySymbol =
                    positions
                    |> Seq.groupBy (fun value -> value.Symbol.ToUpperInvariant())
                    |> Seq.map (fun (symbol, values) -> symbol, values |> Seq.sumBy (fun value -> value.Quantity))
                    |> Map.ofSeq
                let divergence = canonicalBySymbol <> brokerBySymbol
                use state = connection.CreateCommand()
                state.Transaction <- transaction
                state.CommandText <- "INSERT INTO state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value; UPDATE state SET value=$at WHERE key='last_broker_reconciliation_at'"
                state.Parameters.AddWithValue("$key", "portfolio_divergence:" + accountId) |> ignore
                state.Parameters.AddWithValue("$value", if divergence then "true" else "false") |> ignore
                state.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
                state.ExecuteNonQuery() |> ignore
            for identity, position in canonical do
                match positions |> Seq.tryFind (fun value ->
                    value.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase)) with
                | None -> ()
                | Some evidence ->
                    let updated = TradingPortfolioProjectionPolicy.ApplyBrokerMarket(position, evidence)
                    use update = connection.CreateCommand()
                    update.Transaction <- transaction
                    update.CommandText <- "UPDATE canonical_positions SET payload_json=$payload,version=version+1 WHERE identity=$id"
                    update.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(updated, this.Json)) |> ignore
                    update.Parameters.AddWithValue("$id", identity) |> ignore
                    update.ExecuteNonQuery() |> ignore
            use upsertAccount = connection.CreateCommand()
            upsertAccount.Transaction <- transaction
            upsertAccount.CommandText <- """INSERT INTO broker_accounts VALUES($id,$payload,$at)
ON CONFLICT(account_id) DO UPDATE SET payload_json=excluded.payload_json,observed_at=excluded.observed_at"""
            upsertAccount.Parameters.AddWithValue("$id", accountId) |> ignore
            upsertAccount.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(account, this.Json)) |> ignore
            upsertAccount.Parameters.AddWithValue("$at", account.ObservedAtUtc.ToString("O")) |> ignore
            upsertAccount.ExecuteNonQuery() |> ignore
            use clearPositions = connection.CreateCommand()
            clearPositions.Transaction <- transaction
            clearPositions.CommandText <- "DELETE FROM broker_positions WHERE account_id=$account"
            clearPositions.Parameters.AddWithValue("$account", accountId) |> ignore
            clearPositions.ExecuteNonQuery() |> ignore
            for position in positions |> Seq.sortBy (fun value -> value.Symbol) do
                use savePosition = connection.CreateCommand()
                savePosition.Transaction <- transaction
                savePosition.CommandText <- "INSERT INTO broker_positions VALUES($account,$symbol,$payload,$at)"
                savePosition.Parameters.AddWithValue("$account", accountId) |> ignore
                savePosition.Parameters.AddWithValue("$symbol", position.Symbol.ToUpperInvariant()) |> ignore
                savePosition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(position, this.Json)) |> ignore
                savePosition.Parameters.AddWithValue("$at", account.ObservedAtUtc.ToString("O")) |> ignore
                savePosition.ExecuteNonQuery() |> ignore
            transaction.Commit()
            this.RefreshRisk(dailyLossLimitPercent)

        member this.FinancialStateReady() =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT COUNT(*) FROM financial_intents WHERE status IN ($pending,$awaiting,$reconcile)"
            command.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            command.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            command.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            Convert.ToInt64(command.ExecuteScalar()) = 0L
            && (use divergence = connection.CreateCommand()
                divergence.CommandText <- "SELECT COUNT(*) FROM state WHERE key LIKE 'portfolio_divergence:%' AND value='true'"
                Convert.ToInt64(divergence.ExecuteScalar()) = 0L)
