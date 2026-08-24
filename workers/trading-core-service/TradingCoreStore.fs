namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

type TradingCoreStore(config: ServiceConfig, json: JsonSerializerOptions, secrets: SecretStore) =
    do Database.initialize config.DatabasePath config.InitialMode

    member internal _.Connect() = Database.connect config.DatabasePath
    member internal _.Json = json
    member internal _.Secrets = secrets
    member internal _.StateValue(connection: SqliteConnection, key: string) =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM state WHERE key=$key"
        command.Parameters.AddWithValue("$key", key) |> ignore
        Convert.ToString(command.ExecuteScalar())
