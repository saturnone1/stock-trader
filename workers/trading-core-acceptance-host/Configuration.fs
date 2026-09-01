namespace StockTrader.TradingCoreAcceptance

open System
open System.IO
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCoreService

type AcceptanceConfig =
    { Runtime: ServiceConfig
      BrokerEndpoint: Uri
      BrokerClientCertificatePath: string
      BrokerClientKeyPath: string
      BrokerServerCaPath: string
      BrokerServerCommonName: string
      ClockPath: string }

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
        let encryptionKey = Convert.FromBase64String(required "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY")
        if encryptionKey.Length <> 32 then failwith "acceptance encryption key must be 32 bytes"
        let runtime =
            { DatabasePath = Path.Combine(root, "trading-core.db")
              ServerCertificatePath = required "STOCKTRADER_TRADING_CORE_SERVER_CERT_PATH"
              ServerCertificateKeyPath = required "STOCKTRADER_TRADING_CORE_SERVER_KEY_PATH"
              ClientCaPath = required "STOCKTRADER_TRADING_CORE_CLIENT_CA_PATH"
              ClientRoleDnsName = required "STOCKTRADER_ACCEPTANCE_DRIVER_ROLE_DNS"
              CoordinatorRoleDnsName = required "STOCKTRADER_ACCEPTANCE_DRIVER_ROLE_DNS"
              MarketDataEndpoint = Uri(required "STOCKTRADER_MARKET_DATA_ENDPOINT")
              MarketDataClientCertificatePath = required "STOCKTRADER_MARKET_DATA_CLIENT_CERT_PATH"
              MarketDataClientKeyPath = required "STOCKTRADER_MARKET_DATA_CLIENT_KEY_PATH"
              MarketDataServerCaPath = required "STOCKTRADER_MARKET_DATA_SERVER_CA_PATH"
              MarketDataServerCommonName = required "STOCKTRADER_MARKET_DATA_SERVER_COMMON_NAME"
              PositionEvaluationInterval = TimeSpan.FromSeconds(5.0)
              EncryptionKey = encryptionKey
              EncryptionKeyGeneration = required "STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY_GENERATION"
              BrokerCapabilityEnabled = true
              InitialMode = TradingAuthorityMode.Projection }
        { Runtime = runtime
          BrokerEndpoint = Uri(required "STOCKTRADER_ACCEPTANCE_BROKER_ENDPOINT")
          BrokerClientCertificatePath = required "STOCKTRADER_ACCEPTANCE_BROKER_CLIENT_CERT_PATH"
          BrokerClientKeyPath = required "STOCKTRADER_ACCEPTANCE_BROKER_CLIENT_KEY_PATH"
          BrokerServerCaPath = required "STOCKTRADER_ACCEPTANCE_BROKER_SERVER_CA_PATH"
          BrokerServerCommonName = required "STOCKTRADER_ACCEPTANCE_BROKER_SERVER_COMMON_NAME"
          ClockPath = Path.Combine(root, "acceptance-clock.json") }
