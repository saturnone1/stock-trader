module StockTrader.OptimizationWorker.ShadowLeaseProcessor

open System
open System.Text.Json
open System.Threading
open StockTrader.Optimization.Protocol
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.Optimization
open StockTrader.OptimizationWorker.ControlPlaneClient
open StockTrader.OptimizationWorker.WorkerState

let private compatibilityError (lease: OptimizationWorkLease) =
    match OptimizationLeaseCompatibilityPolicy.Error lease with
    | null -> StrategyExecutionArtifactPolicy.CompatibilityError lease.Input.Strategy |> Option.ofObj
    | error -> Some error

let private validationResult (lease: OptimizationWorkLease) =
    let input = lease.Input
    OptimizationWorkerValidationResult(
        OptimizationWorkerContractCatalog.ResultVersion,
        lease.Purpose,
        input.InputHash,
        input.Strategy.ContentHash,
        input.DataEvidence.EvidenceId,
        input.PreparedData.DataHash,
        input.PreparedData.Series.Count,
        input.PreparedData.Series |> Seq.sumBy (fun series -> series.Bars.Count))

let run (control: Client) (state: ProbeState) (lease: OptimizationWorkLease) (ct: CancellationToken) = task {
    match compatibilityError lease with
    | Some error -> return Error error
    | None ->
        state.Lease()
        let heartbeat = OptimizationWorkerHeartbeat(
            OptimizationWorkerContractCatalog.HeartbeatVersion,
            lease.LeaseId, lease.JobId, lease.LeaseGeneration, lease.CancellationGeneration,
            lease.Input.InputHash, 0L, DateTime.UtcNow)
        let! heartbeatResponse = control.PostAsync("/leases/heartbeat", heartbeat, ct)
        match heartbeatResponse with
        | Error error -> return Error error
        | Ok body ->
            match JsonSerializer.Deserialize<OptimizationWorkerHeartbeatReceipt>(body, control.Json)
                  |> Option.ofObj with
            | None -> return Error "heartbeat-empty-response"
            | Some receipt when not receipt.Continue -> return Error ("heartbeat-" + receipt.Reason)
            | Some _ ->
                state.Heartbeat()
                let resultJson = JsonSerializer.Serialize(validationResult lease, control.Json)
                let submission = OptimizationWorkerResultSubmission(
                    OptimizationWorkerContractCatalog.ResultVersion,
                    lease.LeaseId + ":validation:v1",
                    lease.LeaseId, lease.JobId, lease.LeaseGeneration, lease.CancellationGeneration,
                    lease.Input.InputHash, CanonicalJsonHash.Compute(resultJson), resultJson, DateTime.UtcNow)
                let! resultResponse = control.PostAsync("/leases/result", submission, ct)
                match resultResponse with
                | Error error -> return Error error
                | Ok resultBody ->
                    match JsonSerializer.Deserialize<OptimizationWorkerResultReceipt>(
                              resultBody, control.Json) |> Option.ofObj with
                    | None -> return Error "result-empty-response"
                    | Some receipt when receipt.Acceptance = OptimizationResultAcceptance.Accepted
                                         || receipt.Acceptance = OptimizationResultAcceptance.Duplicate ->
                        state.Result()
                        return Ok ()
                    | Some receipt -> return Error ("result-" + receipt.Acceptance.ToString())
}
