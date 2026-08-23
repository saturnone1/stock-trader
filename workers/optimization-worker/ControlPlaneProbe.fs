module StockTrader.OptimizationWorker.ControlPlaneProbe

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.OptimizationWorker.ControlPlaneClient
open StockTrader.OptimizationWorker.WorkerState

type Worker(
    http: HttpClient,
    configuration: IConfiguration,
    state: ProbeState,
    logger: ILogger<Worker>) =
    inherit BackgroundService()

    override _.ExecuteAsync(ct: CancellationToken) = task {
        let settings =
            configuration["STOCKTRADER_CONTROL_API_URL"] |> Option.ofObj,
            configuration["STOCKTRADER_WORKER_ID"] |> Option.ofObj,
            configuration["STOCKTRADER_WORKER_SECRET"] |> Option.ofObj
        match settings with
        | Some baseUrl, Some workerId, Some secret
            when not (String.IsNullOrWhiteSpace(baseUrl))
              && not (String.IsNullOrWhiteSpace(workerId))
              && not (String.IsNullOrWhiteSpace(secret)) ->
            state.Configure()
            let control = Client(http, baseUrl, workerId, secret)
            while not ct.IsCancellationRequested do
                state.Attempt()
                try
                    let! claim = control.ClaimAsync(ct)
                    match claim with
                    | Ok None -> state.Succeed()
                    | Error error ->
                        state.Fail(error)
                        logger.LogWarning("Control API request failed: {Failure}", error)
                    | Ok (Some lease) ->
                        let! result = ShadowLeaseProcessor.run control state lease ct
                        match result with
                        | Ok () ->
                            state.Succeed()
                            logger.LogInformation(
                                "Shadow lease {LeaseId} contract validation submitted", lease.LeaseId)
                        | Error error ->
                            state.Fail(error)
                            logger.LogWarning(
                                "Shadow lease {LeaseId} failed: {Failure}", lease.LeaseId, error)
                with
                | :? OperationCanceledException when ct.IsCancellationRequested -> ()
                | error ->
                    let failure = error.GetType().Name
                    state.Fail(failure)
                    logger.LogWarning(error, "Control API request failed: {Failure}", failure)
                if not ct.IsCancellationRequested then do! Task.Delay(10_000, ct)
        | _ ->
            logger.LogInformation("Control API is not configured; shadow host remains isolated")
    }
