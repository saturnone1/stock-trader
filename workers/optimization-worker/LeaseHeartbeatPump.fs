module StockTrader.OptimizationWorker.LeaseHeartbeatPump

open System
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open StockTrader.ServiceContracts.Optimization
open StockTrader.OptimizationWorker.ControlPlaneClient
open StockTrader.OptimizationWorker.WorkerState

type Pump(control: Client, state: ProbeState, lease: OptimizationWorkLease, parentCt: CancellationToken) =
    let cancellation = CancellationTokenSource.CreateLinkedTokenSource(parentCt)
    let mutable failure: string option = None
    let interval =
        let seconds = Math.Max(2.0, Math.Min(15.0, (lease.ExpiresAt - lease.LeasedAt).TotalSeconds / 3.0))
        TimeSpan.FromSeconds(seconds)

    let heartbeat tested = OptimizationWorkerHeartbeat(
        OptimizationWorkerContractCatalog.HeartbeatVersion,
        lease.LeaseId, lease.JobId, lease.LeaseGeneration, lease.CancellationGeneration,
        lease.Input.InputHash, tested, DateTime.UtcNow)

    member _.Token = cancellation.Token
    member _.Failure = failure

    member _.Start() = task {
        try
            while not cancellation.IsCancellationRequested do
                do! Task.Delay(interval, cancellation.Token)
                let! response = control.PostAsync("/leases/heartbeat", heartbeat 0L, cancellation.Token)
                match response with
                | Error error ->
                    failure <- Some error
                    cancellation.Cancel()
                | Ok body ->
                    match JsonSerializer.Deserialize<OptimizationWorkerHeartbeatReceipt>(body, control.Json)
                          |> Option.ofObj with
                    | Some receipt when receipt.Continue -> state.Heartbeat()
                    | Some receipt ->
                        failure <- Some ("heartbeat-" + receipt.Reason)
                        cancellation.Cancel()
                    | None ->
                        failure <- Some "heartbeat-empty-response"
                        cancellation.Cancel()
        with
        | :? OperationCanceledException -> ()
    }

    member _.StopAsync(running: Task) = task {
        cancellation.Cancel()
        do! running
        cancellation.Dispose()
    }
