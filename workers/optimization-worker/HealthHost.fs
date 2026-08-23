module StockTrader.OptimizationWorker.HealthHost

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Configuration
open StockTrader.ServiceContracts.Optimization
open StockTrader.OptimizationWorker.ControlPlaneProbe
open StockTrader.OptimizationWorker.WorkerState
open StockTrader.OptimizationWorker.MutualTlsHttpClient

let private status mode state (probe: ProbeSnapshot) =
    {| service = "optimization-worker"
       mode = mode
       status = state
       controlConfigured = probe.Configured
       controlConnected = probe.Connected
       controlError = probe.LastError
       contractVersion = OptimizationWorkerContractCatalog.LeaseVersion |}

let run (_: string array) =
    let builder = WebApplication.CreateBuilder(Array.empty<string>)
    let mode =
        builder.Configuration["STOCKTRADER_WORKER_MODE"]
        |> Option.ofObj
        |> Option.defaultValue "remote"
    builder.Services.AddSingleton<ProbeState>() |> ignore
    builder.Services.AddSingleton<HttpClient>(fun services ->
        create (services.GetRequiredService<IConfiguration>())) |> ignore
    builder.Services.AddHostedService<Worker>() |> ignore
    let app = builder.Build()
    let probe = app.Services.GetRequiredService<ProbeState>()

    app.MapGet("/health/live", Func<IResult>(fun () -> Results.Json(status mode "live" (probe.Snapshot()))))
    |> ignore
    app.MapGet("/health/ready", Func<IResult>(fun () ->
        let snapshot = probe.Snapshot()
        if not snapshot.Configured || snapshot.Connected then
            Results.Json(status mode "ready" snapshot)
        else
            Results.Json(status mode "not-ready" snapshot, statusCode = 503)))
    |> ignore
    app.MapGet(
        "/metrics",
        Func<IResult>(fun () ->
            let snapshot = probe.Snapshot()
            let connected = if snapshot.Connected then 1 else 0
            let ready = if not snapshot.Configured || snapshot.Connected then 1 else 0
            let active = if snapshot.ActiveLease then 1 else 0
            let body = $"stocktrader_optimization_worker_ready {ready}\nstocktrader_optimization_worker_control_connected {connected}\nstocktrader_optimization_worker_control_attempts_total {snapshot.Attempts}\nstocktrader_optimization_worker_control_successes_total {snapshot.Successes}\nstocktrader_optimization_worker_leases_total {snapshot.Leases}\nstocktrader_optimization_worker_active_lease {active}\nstocktrader_optimization_worker_heartbeats_total {snapshot.Heartbeats}\nstocktrader_optimization_worker_results_total {snapshot.Results}\nstocktrader_optimization_worker_failures_total {snapshot.Failures}\nstocktrader_optimization_worker_cancellations_total {snapshot.Cancellations}\nstocktrader_optimization_worker_contract_version {OptimizationWorkerContractCatalog.LeaseVersion}\n"
            Results.Text(body, "text/plain; version=0.0.4")))
    |> ignore

    app.Run()
    0
