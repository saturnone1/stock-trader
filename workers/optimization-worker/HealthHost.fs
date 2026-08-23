module StockTrader.OptimizationWorker.HealthHost

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open StockTrader.ServiceContracts.Optimization

let private status () =
    {| service = "optimization-worker"
       mode = "shadow"
       status = "ready"
       contractVersion = OptimizationWorkerContractCatalog.LeaseVersion |}

let run (_: string array) =
    let builder = WebApplication.CreateBuilder(Array.empty<string>)
    let app = builder.Build()

    app.MapGet("/health/live", Func<IResult>(fun () -> Results.Json(status ())))
    |> ignore
    app.MapGet("/health/ready", Func<IResult>(fun () -> Results.Json(status ())))
    |> ignore
    app.MapGet(
        "/metrics",
        Func<IResult>(fun () ->
            let body =
                $"stocktrader_optimization_worker_ready 1\nstocktrader_optimization_worker_contract_version {OptimizationWorkerContractCatalog.LeaseVersion}\n"
            Results.Text(body, "text/plain; version=0.0.4")))
    |> ignore

    app.Run()
    0
