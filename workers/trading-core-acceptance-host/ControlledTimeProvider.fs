namespace StockTrader.TradingCoreAcceptance

open System
open System.IO
open System.Text.Json
open StockTrader.ServiceContracts.TradingCore

type private ClockState =
    { ScenarioId: string
      UtcNow: DateTime
      Revision: int64
      Operations: string array }

type ControlledTimeProvider(path: string, initialUtc: DateTime) =
    inherit TimeProvider()
    let sync = obj ()
    let json = JsonSerializerOptions(JsonSerializerDefaults.Web)
    let mutable state =
        if File.Exists path then
            JsonSerializer.Deserialize<ClockState>(File.ReadAllText path, json)
        else
            { ScenarioId = ""; UtcNow = initialUtc; Revision = 0L; Operations = [||] }

    let persist value =
        Directory.CreateDirectory(Path.GetDirectoryName path) |> ignore
        let temporary = path + ".tmp"
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, json))
        File.Move(temporary, path, true)

    override _.GetUtcNow() = DateTimeOffset(state.UtcNow)

    member _.View() = lock sync (fun () ->
        AcceptanceTimeView(state.ScenarioId, state.UtcNow, state.Revision))

    member _.Advance(request: AcceptanceTimeAdvanceRequest) = lock sync (fun () ->
        if not (Guid.TryParse(request.ScenarioId) |> fst)
           || String.IsNullOrWhiteSpace request.OperationId
           || request.UtcNow.Kind <> DateTimeKind.Utc then
            invalidArg "request" "invalid-acceptance-time-operation"
        if state.Operations |> Array.contains request.OperationId then
            AcceptanceTimeView(state.ScenarioId, state.UtcNow, state.Revision)
        else
            if state.ScenarioId <> "" && state.ScenarioId <> request.ScenarioId then
                invalidOp "acceptance-clock-scenario-conflict"
            if request.UtcNow < state.UtcNow then invalidOp "acceptance-time-reversal"
            state <-
                { ScenarioId = request.ScenarioId
                  UtcNow = request.UtcNow
                  Revision = state.Revision + 1L
                  Operations = Array.append state.Operations [| request.OperationId |] }
            persist state
            AcceptanceTimeView(state.ScenarioId, state.UtcNow, state.Revision))
