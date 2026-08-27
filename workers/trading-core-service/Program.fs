open StockTrader.TradingCoreService

[<EntryPoint>]
let main args =
    if args |> Array.contains "--rotate-encryption-key" then
        EncryptionKeyMigration.run ()
    else
        HttpHost.run args
