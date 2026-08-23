module StockTrader.OptimizationWorker.HealthHost

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open StockTrader.ServiceContracts.Optimization
open StockTrader.OptimizationWorker.ControlPlaneProbe

let private status (probe: ProbeSnapshot) =
    {| service = "optimization-worker"
       mode = "shadow"
       status = "ready"
       controlConfigured = probe.Configured
       controlConnected = probe.Connected
       contractVersion = OptimizationWorkerContractCatalog.LeaseVersion |}

let run (_: string array) =
    let builder = WebApplication.CreateBuilder(Array.empty<string>)
    builder.Services.AddSingleton<ProbeState>() |> ignore
    builder.Services.AddSingleton<HttpClient>() |> ignore
    builder.Services.AddHostedService<Worker>() |> ignore
    let app = builder.Build()
    let probe = app.Services.GetRequiredService<ProbeState>()

    app.MapGet("/health/live", Func<IResult>(fun () -> Results.Json(status (probe.Snapshot()))))
    |> ignore
    app.MapGet("/health/ready", Func<IResult>(fun () -> Results.Json(status (probe.Snapshot()))))
    |> ignore
    app.MapGet(
        "/metrics",
        Func<IResult>(fun () ->
            let snapshot = probe.Snapshot()
            let connected = if snapshot.Connected then 1 else 0
            let body = $"stocktrader_optimization_worker_ready 1\nstocktrader_optimization_worker_control_connected {connected}\nstocktrader_optimization_worker_control_attempts_total {snapshot.Attempts}\nstocktrader_optimization_worker_control_successes_total {snapshot.Successes}\nstocktrader_optimization_worker_contract_version {OptimizationWorkerContractCatalog.LeaseVersion}\n"
            Results.Text(body, "text/plain; version=0.0.4")))
    |> ignore

    app.Run()
    0
