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
            TradingCorePortfolioView(
                TradingCoreContractVersions.Current, authority.Generation,
                StoreQueries.readCanonical<TradingRecommendationProjection>
                    connection this.Json "canonical_recommendations",
                StoreQueries.readCanonical<TradingPositionProjection>
                    connection this.Json "canonical_positions",
                StoreQueries.readCanonical<TradingTradeProjection>
                    connection this.Json "canonical_trades")
    
        member this.Status() =
            use connection = this.Connect()
            let count sql =
                use command = connection.CreateCommand()
                command.CommandText <- sql
                Convert.ToInt64(command.ExecuteScalar())
            let authority = this.Authority()
            TradingCoreStatus(TradingCoreContractVersions.Current, true, authority.Mode,
                authority.Generation, Int64.Parse(this.StateValue(connection, "account_generation")),
                count "SELECT COUNT(*) FROM inbox", count "SELECT COUNT(*) FROM outbox WHERE delivered_at IS NULL",
                this.StateValue(connection, "last_snapshot_id"), Nullable(), null)
