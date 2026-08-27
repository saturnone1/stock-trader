namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreQueryStore =
    module private StoreQueries =
        let readCanonical<'T when 'T : not struct and 'T : not null>
            (connection: SqliteConnection) (json: JsonSerializerOptions) table =
            use command = connection.CreateCommand()
            command.CommandText <- $"SELECT payload_json FROM {table} ORDER BY identity"
            use reader = command.ExecuteReader()
            let values = ResizeArray<'T>()
            while reader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<'T>(reader.GetString 0, json)) with
                | Some value -> values.Add value
                | None -> invalidOp $"empty-canonical-{table}-payload"
            values.ToArray() :> IReadOnlyList<'T>

        let readProjected<'T when 'T : not struct and 'T : not null>
            (connection: SqliteConnection) (json: JsonSerializerOptions) kind =
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM projections WHERE kind=$kind ORDER BY identity"
            command.Parameters.AddWithValue("$kind", kind) |> ignore
            use reader = command.ExecuteReader()
            let values = ResizeArray<'T>()
            while reader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<'T>(reader.GetString 0, json)) with
                | Some value -> values.Add value
                | None -> invalidOp $"empty-projected-{kind}-payload"
            values.ToArray() :> IReadOnlyList<'T>

        let readSingle<'T when 'T : not struct and 'T : not null>
            (connection: SqliteConnection) (json: JsonSerializerOptions) sql =
            use command = connection.CreateCommand()
            command.CommandText <- sql
            match command.ExecuteScalar() with
            | null -> invalidOp "missing-trading-core-risk-projection"
            | payload ->
                match Option.ofObj (JsonSerializer.Deserialize<'T>(Convert.ToString payload, json)) with
                | Some value -> value
                | None -> invalidOp "empty-trading-core-risk-projection"

        let readBrokerAccounts
            (connection: SqliteConnection) (json: JsonSerializerOptions) =
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM broker_accounts ORDER BY account_id"
            use reader = command.ExecuteReader()
            let evidence = ResizeArray<BrokerAccountEvidence>()
            while reader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<BrokerAccountEvidence>(reader.GetString 0, json)) with
                | Some value -> evidence.Add value
                | None -> invalidOp "empty-broker-account-projection"
            reader.Close()
            use positionsCommand = connection.CreateCommand()
            positionsCommand.CommandText <- "SELECT payload_json FROM canonical_positions WHERE json_extract(payload_json,'$.closedAtUtc') IS NULL"
            use positionReader = positionsCommand.ExecuteReader()
            let positions = ResizeArray<TradingPositionProjection>()
            while positionReader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<TradingPositionProjection>(positionReader.GetString 0, json)) with
                | Some value -> positions.Add value
                | None -> invalidOp "empty-position-account-projection"
            evidence
            |> Seq.map (fun account ->
                let unrealized =
                    positions
                    |> Seq.filter (fun position -> position.AccountId = account.AccountId)
                    |> Seq.sumBy (fun position ->
                        (position.CurrentPrice - position.EntryPrice) * decimal position.Quantity)
                TradingBrokerAccountProjection(
                    account.AccountId, account.TotalEquity, account.Cash, account.BuyingPower,
                    unrealized, account.TotalEquity - account.PreviousDayEquity,
                    account.IsTradingBlocked, account.ObservedAtUtc))
            |> Seq.toArray
            :> IReadOnlyList<TradingBrokerAccountProjection>
    
    type TradingCoreStore with
        member this.PositionRiskEvidence() =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM canonical_positions"
            use reader = command.ExecuteReader()
            let positions = ResizeArray<TradingPositionRiskEvidence>()
            while reader.Read() do
                match Option.ofObj (JsonSerializer.Deserialize<TradingPositionProjection>(reader.GetString 0, this.Json)) with
                | Some position when not position.ClosedAtUtc.HasValue ->
                    positions.Add(TradingPositionRiskEvidence(position.Symbol, position.Sector))
                | _ -> ()
            positions.ToArray() :> IReadOnlyList<TradingPositionRiskEvidence>
    
        member this.Portfolio() =
            use connection = this.Connect()
            let authority = this.Authority()
            if authority.Mode = TradingAuthorityMode.Remote then
                TradingCorePortfolioView(
                    TradingCoreContractVersions.Current, authority.Generation,
                    StoreQueries.readCanonical<TradingRecommendationProjection>
                        connection this.Json "canonical_recommendations",
                    StoreQueries.readCanonical<TradingPositionProjection>
                        connection this.Json "canonical_positions",
                    StoreQueries.readCanonical<TradingTradeProjection>
                        connection this.Json "canonical_trades",
                    StoreQueries.readSingle<TradingRiskProjection> connection this.Json
                        "SELECT payload_json FROM canonical_risk WHERE singleton=1",
                    StoreQueries.readBrokerAccounts connection this.Json)
            else
                TradingCorePortfolioView(
                    TradingCoreContractVersions.Current, authority.Generation,
                    StoreQueries.readProjected<TradingRecommendationProjection>
                        connection this.Json "recommendation",
                    StoreQueries.readProjected<TradingPositionProjection>
                        connection this.Json "position",
                    StoreQueries.readProjected<TradingTradeProjection>
                        connection this.Json "trade",
                    StoreQueries.readSingle<TradingRiskProjection> connection this.Json
                        "SELECT payload_json FROM projections WHERE kind='risk' AND identity='portfolio'",
                    Array.Empty<TradingBrokerAccountProjection>())
    
        member this.Status() =
            use connection = this.Connect()
            let count sql =
                use command = connection.CreateCommand()
                command.CommandText <- sql
                Convert.ToInt64(command.ExecuteScalar())
            let authority = this.Authority()
            let reconciledAt = this.StateValue(connection, "last_broker_reconciliation_at")
            let hasDivergence =
                use divergence = connection.CreateCommand()
                divergence.CommandText <- "SELECT COUNT(*) FROM state WHERE key LIKE 'portfolio_divergence:%' AND value='true'"
                Convert.ToInt64(divergence.ExecuteScalar()) > 0L
            TradingCoreStatus(TradingCoreContractVersions.Current, true, authority.Mode,
                authority.Generation, Int64.Parse(this.StateValue(connection, "account_generation")),
                count "SELECT COUNT(*) FROM inbox", count "SELECT COUNT(*) FROM outbox WHERE delivered_at IS NULL",
                this.StateValue(connection, "last_snapshot_id"),
                (if String.IsNullOrWhiteSpace reconciledAt then Nullable()
                 else Nullable(DateTime.Parse(reconciledAt, null,
                    Globalization.DateTimeStyles.RoundtripKind))),
                (if hasDivergence then "broker-canonical-portfolio-divergence" else null))
