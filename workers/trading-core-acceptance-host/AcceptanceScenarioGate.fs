namespace StockTrader.TradingCoreAcceptance

open System
open System.IO
open System.Text.Json
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCoreService

type private PersistedAcceptanceScenario =
    { View: AcceptanceScenarioState
      RunningAuthority: TradingAuthorityContract }

type AcceptanceScenarioGate(path: string, store: TradingCoreStore,
                            json: JsonSerializerOptions, clock: TimeProvider) =
    let sync = obj ()
    let emptyView = AcceptanceScenarioState("", "Empty", "", Array.empty,
                                             clock.GetUtcNow().UtcDateTime)
    let mutable current: PersistedAcceptanceScenario option =
        if File.Exists path then
            Some(JsonSerializer.Deserialize<PersistedAcceptanceScenario>(File.ReadAllText path, json))
        else None

    let persist value =
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        let temporary = path + ".tmp"
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, json))
        File.Move(temporary, path, true)

    let validateIdentity (request: AcceptanceBootstrapRequest) =
        if not (Guid.TryParse(request.ScenarioId) |> fst)
           || String.IsNullOrWhiteSpace request.OperationId
           || String.IsNullOrWhiteSpace request.FixtureHash
           || not (request.Snapshot.SnapshotId.StartsWith("acceptance-", StringComparison.Ordinal))
           || not (request.RunningAuthority.AuthorityId.StartsWith("acceptance-", StringComparison.Ordinal))
           || request.RunningAuthority.Mode <> TradingAuthorityMode.Remote
           || request.AccountConfiguration.Accounts.Count = 0
           || (request.AccountConfiguration.Accounts
               |> Seq.exists (fun account ->
                   not (account.AccountId.StartsWith("acceptance-", StringComparison.Ordinal)))) then
            invalidArg "request" "invalid-acceptance-bootstrap"

    member _.View() = lock sync (fun () ->
        current |> Option.map _.View |> Option.defaultValue emptyView)

    member _.Bootstrap(request: AcceptanceBootstrapRequest) = lock sync (fun () ->
        validateIdentity request
        match current with
        | Some value when value.View.OperationIds |> Seq.contains request.OperationId -> value.View
        | Some _ -> invalidOp "acceptance-bootstrap-closed"
        | None ->
            store.Import request.Snapshot |> ignore
            store.ApplyAccountConfiguration request.AccountConfiguration |> ignore
            let view = AcceptanceScenarioState(request.ScenarioId, "Prepared", request.FixtureHash,
                                                [| request.OperationId |], clock.GetUtcNow().UtcDateTime)
            let value = { View = view; RunningAuthority = request.RunningAuthority }
            persist value
            current <- Some value
            view)

    member _.Start(request: AcceptanceScenarioStartRequest) = lock sync (fun () ->
        match current with
        | None -> invalidOp "acceptance-scenario-not-prepared"
        | Some value when value.View.ScenarioId <> request.ScenarioId ->
            invalidOp "acceptance-scenario-conflict"
        | Some value when value.View.OperationIds |> Seq.contains request.OperationId -> value.View
        | Some value when value.View.Phase <> "Prepared" ->
            invalidOp "acceptance-scenario-not-prepared"
        | Some value ->
            store.Activate value.RunningAuthority
            let view = AcceptanceScenarioState(value.View.ScenarioId, "Running", value.View.FixtureHash,
                Array.append (value.View.OperationIds |> Seq.toArray) [| request.OperationId |],
                clock.GetUtcNow().UtcDateTime)
            let updated = { value with View = view }
            persist updated
            current <- Some updated
            view)
