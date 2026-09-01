open System.Threading
open StockTrader.TradingCoreCutoverCoordinator

[<EntryPoint>]
let main _ = Coordinator.run CancellationToken.None |> Async.AwaitTask |> Async.RunSynchronously
