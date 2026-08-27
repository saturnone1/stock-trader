namespace StockTrader.TradingCoreService

open System
open System.IO
open StockTrader.ServiceContracts.TradingCore

type ServiceConfig =
    { DatabasePath: string
      ServerCertificatePath: string
      ServerCertificateKeyPath: string
      ClientCaPath: string
      ClientRoleDnsName: string
      MarketDataEndpoint: Uri
      MarketDataClientCertificatePath: string
      MarketDataClientKeyPath: string
      MarketDataServerCaPath: string
      MarketDataServerCommonName: string
      PositionEvaluationInterval: TimeSpan
      EncryptionKey: byte array
      EncryptionKeyGeneration: string
      InitialMode: TradingAuthorityMode }

type EncryptionMigrationConfig =
    { DatabasePath: string
      OldKey: byte array
      OldGeneration: string
      NewKey: byte array
      NewGeneration: string }

module Configuration =
    let private required name =
        match Environment.GetEnvironmentVariable name with
        | null | "" -> failwith $"Missing required setting: {name}"
        | value -> value

    let private dataRoot () =
        match Environment.GetEnvironmentVariable "STOCKTRADER_TRADING_CORE_DATA" with
        | null | "" -> "/data"
        | value -> value

    let private key name =
        let bytes = Convert.FromBase64String(required name)
        if bytes.Length <> 32 then failwith $"{name} must decode to 32 bytes"
        bytes

    let private generation name =
        let value = required name
        if value.Length > 14 || not (Char.IsLower value[0] || Char.IsDigit value[0])
           || value |> Seq.exists (fun c -> not (Char.IsLower c || Char.IsDigit c || c = '-')) then
            failwith $"{name} must be 1-14 lowercase letters, digits, or hyphens"
        value

    let load () =
        let root = dataRoot ()
        let initialMode =
            match Environment.GetEnvironmentVariable "STOCKTRADER_TRADING_CORE_MODE" with
            | null | "" -> TradingAuthorityMode.Projection
            | raw ->
                match Enum.TryParse<TradingAuthorityMode>(raw, true) with
                | true, value -> value
                | _ -> failwith "Invalid STOCKTRADER_TRADING_CORE_MODE"
        { DatabasePath = Path.Combine(root, "trading-core.db")
          ServerCertificatePath = required "STOCKTRADER_TRADING_CORE_SERVER_CERT_PATH"
          ServerCertificateKeyPath = required "STOCKTRADER_TRADING_CORE_SERVER_KEY_PATH"
          ClientCaPath = required "STOCKTRADER_TRADING_CORE_CLIENT_CA_PATH"
          ClientRoleDnsName =
            match Environment.GetEnvironmentVariable "STOCKTRADER_TRADING_CORE_CLIENT_ROLE_DNS" with
            | null | "" -> "edge-trading-control.stocktrader.internal"
            | value -> value
          MarketDataEndpoint = Uri(required "STOCKTRADER_MARKET_DATA_ENDPOINT")
          MarketDataClientCertificatePath = required "STOCKTRADER_MARKET_DATA_CLIENT_CERT_PATH"
          MarketDataClientKeyPath = required "STOCKTRADER_MARKET_DATA_CLIENT_KEY_PATH"
          MarketDataServerCaPath = required "STOCKTRADER_MARKET_DATA_SERVER_CA_PATH"
          MarketDataServerCommonName = required "STOCKTRADER_MARKET_DATA_SERVER_COMMON_NAME"
          PositionEvaluationInterval =
            let raw =
                match Environment.GetEnvironmentVariable "STOCKTRADER_POSITION_EVALUATION_INTERVAL_SECONDS" with
                | null | "" -> 30
                | value -> Int32.Parse value
            if raw < 5 || raw > 3600 then
                failwith "STOCKTRADER_POSITION_EVALUATION_INTERVAL_SECONDS must be between 5 and 3600"
            TimeSpan.FromSeconds(float raw)
          EncryptionKey =
            key "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY"
          EncryptionKeyGeneration =
            match Environment.GetEnvironmentVariable "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY_GENERATION" with
            | null | "" -> "legacy"
            | _ -> generation "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY_GENERATION"
          InitialMode = initialMode }

    let loadEncryptionMigration () =
        { DatabasePath = Path.Combine(dataRoot (), "trading-core.db")
          OldKey = key "STOCKTRADER_TRADING_CORE_OLD_ENCRYPTION_KEY"
          OldGeneration = generation "STOCKTRADER_TRADING_CORE_OLD_ENCRYPTION_GENERATION"
          NewKey = key "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY"
          NewGeneration = generation "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY_GENERATION" }
