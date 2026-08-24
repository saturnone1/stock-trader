namespace StockTrader.TradingCoreService

open System
open System.IO
open StockTrader.ServiceContracts.TradingCore

type ServiceConfig =
    { DatabasePath: string
      SharedSecret: string
      ServerCertificatePath: string
      ServerCertificateKeyPath: string
      ClientCaPath: string
      EncryptionKey: byte array
      InitialMode: TradingAuthorityMode }

module Configuration =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let load () =
        let root =
            match Environment.GetEnvironmentVariable "STOCKTRADER_TRADING_CORE_DATA" with
            | null | "" -> "/data"
            | value -> value
        let initialMode =
            match Environment.GetEnvironmentVariable "STOCKTRADER_TRADING_CORE_MODE" with
            | null | "" -> TradingAuthorityMode.Projection
            | raw ->
                match Enum.TryParse<TradingAuthorityMode>(raw, true) with
                | true, value -> value
                | _ -> failwith "Invalid STOCKTRADER_TRADING_CORE_MODE"
        { DatabasePath = Path.Combine(root, "trading-core.db")
          SharedSecret = required "STOCKTRADER_TRADING_CORE_SECRET"
          ServerCertificatePath = required "STOCKTRADER_TRADING_CORE_SERVER_CERT_PATH"
          ServerCertificateKeyPath = required "STOCKTRADER_TRADING_CORE_SERVER_KEY_PATH"
          ClientCaPath = required "STOCKTRADER_TRADING_CORE_CLIENT_CA_PATH"
          EncryptionKey =
            let bytes = Convert.FromBase64String(required "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY")
            if bytes.Length <> 32 then failwith "Trading Core encryption key must be 32 bytes"
            bytes
          InitialMode = initialMode }
