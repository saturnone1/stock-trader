open System
open System.IO
open System.Text.Json
open StockTrader.ServiceContracts.Optimization

let json = JsonSerializerOptions(JsonSerializerDefaults.Web)

let emit status detail =
    JsonSerializer.Serialize(
        {| service = "optimization-worker"
           mode = "shadow"
           status = status
           detail = detail
           contractVersion = OptimizationWorkerContractCatalog.LeaseVersion |}, json)
    |> Console.WriteLine

let selfCheck () =
    emit "ready" "contract-validation-only"
    0

let validateLease path =
    try
        let lease =
            JsonSerializer.Deserialize<OptimizationWorkLease>(File.ReadAllText path, json)
            |> Option.ofObj
        match lease with
        | None ->
            emit "rejected" "empty-lease"
            2
        | Some lease ->
            match OptimizationLeaseCompatibilityPolicy.Error lease with
            | null ->
                emit "accepted" lease.Input.InputHash
                0
            | error ->
                emit "rejected" error
                2
    with error ->
        emit "invalid" error.Message
        3

[<EntryPoint>]
let main args =
    match args with
    | [||] | [| "--self-check" |] -> selfCheck ()
    | [| "--validate-lease"; path |] -> validateLease path
    | _ ->
        emit "invalid" "usage: --self-check | --validate-lease <file>"
        64
