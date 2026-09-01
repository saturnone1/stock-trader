open System
open StockTrader.TradingCoreAcceptanceDriver

[<EntryPoint>]
let main _ =
    match Environment.GetEnvironmentVariable "STOCKTRADER_ACCEPTANCE_FIXTURE_PATH",
          Environment.GetEnvironmentVariable "STOCKTRADER_ACCEPTANCE_DEFINITION_PATH" with
    | (null | ""), (null | "") -> ManifestDriver.run ()
    | _ -> ScenarioDriver.run ()
