namespace StockTrader.TradingCoreService

open System
open StockTrader.ServiceContracts.TradingCore

type ServiceConfig =
    { DatabasePath: string
      ServerCertificatePath: string
      ServerCertificateKeyPath: string
      ClientCaPath: string
      ClientRoleDnsName: string
      CoordinatorRoleDnsName: string
      MarketDataEndpoint: Uri
      MarketDataClientCertificatePath: string
      MarketDataClientKeyPath: string
      MarketDataServerCaPath: string
      MarketDataServerCommonName: string
      PositionEvaluationInterval: TimeSpan
      EncryptionKey: byte array
      EncryptionKeyGeneration: string
      BrokerCapabilityEnabled: bool
      InitialMode: TradingAuthorityMode }

type EncryptionMigrationConfig =
    { DatabasePath: string
      OldKey: byte array
      OldGeneration: string
      NewKey: byte array
      NewGeneration: string }
