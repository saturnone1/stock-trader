module StockTrader.OptimizationWorker.LeaseProcessor

open System
open System.Text.Json
open System.Threading
open StockTrader.Optimization.Compute
open StockTrader.Optimization.Protocol
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.Optimization
open StockTrader.OptimizationWorker.ControlPlaneClient
open StockTrader.OptimizationWorker.LeaseHeartbeatPump
open StockTrader.OptimizationWorker.WorkerState
let private compatibilityError (lease: OptimizationWorkLease) =
    match OptimizationLeaseCompatibilityPolicy.Error lease with
    | null -> StrategyExecutionArtifactPolicy.CompatibilityError lease.Input.Strategy |> Option.ofObj
    | error -> Some error
let private isComputePurpose purpose =
    purpose = OptimizationWorkerContractCatalog.OptimizationComputePurpose
    || purpose = OptimizationWorkerContractCatalog.ShadowComputePurpose
let private validationResult (lease: OptimizationWorkLease) =
    let input = lease.Input
    OptimizationWorkerValidationResult(
        OptimizationWorkerContractCatalog.ResultVersion, lease.Purpose, input.InputHash,
        input.Strategy.ContentHash, input.DataEvidence.EvidenceId, input.PreparedData.DataHash,
        input.PreparedData.Series.Count,
        input.PreparedData.Series |> Seq.sumBy (fun series -> series.Bars.Count))
let private initialHeartbeat (control: Client) (state: ProbeState)
                             (lease: OptimizationWorkLease) (ct: CancellationToken) = task {
    let heartbeat = OptimizationWorkerHeartbeat(
        OptimizationWorkerContractCatalog.HeartbeatVersion,
        lease.LeaseId, lease.JobId, lease.LeaseGeneration, lease.CancellationGeneration,
        lease.Input.InputHash, 0L, DateTime.UtcNow)
    let! response = control.PostAsync("/leases/heartbeat", heartbeat, ct)
    match response with
    | Error error -> return Error error
    | Ok body ->
        match JsonSerializer.Deserialize<OptimizationWorkerHeartbeatReceipt>(body, control.Json)
              |> Option.ofObj with
        | None -> return Error "heartbeat-empty-response"
        | Some receipt when not receipt.Continue -> return Error ("heartbeat-" + receipt.Reason)
        | Some _ ->
            state.Heartbeat()
            return Ok ()
}
let private computeResult (lease: OptimizationWorkLease) (ct: CancellationToken) = task {
    if isComputePurpose lease.Purpose then
        let! result = OptimizationComputeFacade.ExecuteAsync(lease, ct)
        return JsonSerializer.Serialize(result)
    else
        return JsonSerializer.Serialize(validationResult lease)
}
let private submitResult (control: Client) (state: ProbeState)
                         (lease: OptimizationWorkLease) resultJson ct = task {
    let suffix =
        if isComputePurpose lease.Purpose
        then ":compute:v1" else ":validation:v1"
    let submission = OptimizationWorkerResultSubmission(
        OptimizationWorkerContractCatalog.ResultVersion,
        lease.LeaseId + suffix,
        lease.LeaseId, lease.JobId, lease.LeaseGeneration, lease.CancellationGeneration,
        lease.Input.InputHash, CanonicalJsonHash.Compute(resultJson), resultJson, DateTime.UtcNow)
    let! response = control.PostAsync("/leases/result", submission, ct)
    match response with
    | Error error -> return Error error
    | Ok body ->
        match JsonSerializer.Deserialize<OptimizationWorkerResultReceipt>(body, control.Json)
              |> Option.ofObj with
        | None -> return Error "result-empty-response"
        | Some receipt when receipt.Acceptance = OptimizationResultAcceptance.Accepted
                             || receipt.Acceptance = OptimizationResultAcceptance.Duplicate ->
            state.Result()
            return Ok ()
        | Some receipt -> return Error ("result-" + receipt.Acceptance.ToString())
}
let run (control: Client) (state: ProbeState) (lease: OptimizationWorkLease) (ct: CancellationToken) = task {
    match compatibilityError lease with
    | Some error -> return Error error
    | None ->
        state.Lease()
        let! heartbeat = initialHeartbeat control state lease ct
        match heartbeat with
        | Error error -> return Error error
        | Ok () ->
            let pump = Pump(control, state, lease, ct)
            let running = pump.Start()
            try
                let! resultJson = computeResult lease pump.Token
                do! pump.StopAsync(running)
                match pump.Failure with
                | Some error -> return Error error
                | None -> return! submitResult control state lease resultJson ct
            with
            | :? OperationCanceledException when pump.Failure.IsSome ->
                do! pump.StopAsync(running)
                return Error pump.Failure.Value
            | error ->
                do! pump.StopAsync(running)
                return Error (error.GetType().Name)
}
