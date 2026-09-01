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
    do if config.BrokerCapabilityEnabled then
        use connection = Database.connect config.DatabasePath
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT ciphertext,nonce,tag,encryption_key_generation FROM account_configuration WHERE singleton=1"
        use reader = command.ExecuteReader()
        if reader.Read() then
            let payload =
                { Ciphertext = reader.GetFieldValue<byte array>(0)
                  Nonce = reader.GetFieldValue<byte array>(1)
                  Tag = reader.GetFieldValue<byte array>(2) }
            secrets.Unprotect(payload, reader.GetString(3)) |> ignore

    member internal _.Connect() = Database.connect config.DatabasePath
    member internal _.Json = json
    member internal _.Secrets = secrets
    member internal _.StateValue(connection: SqliteConnection, key: string) =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT value FROM state WHERE key=$key"
        command.Parameters.AddWithValue("$key", key) |> ignore
        Convert.ToString(command.ExecuteScalar())
