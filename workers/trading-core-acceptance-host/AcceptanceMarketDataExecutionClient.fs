namespace StockTrader.TradingCoreAcceptance

open System.Threading
open System.Threading.Tasks
open StockTrader.ServiceContracts.MarketData
open StockTrader.TradingCoreService

/// Acceptance-only fault boundary. It preserves the real persisted bar payload and identity while
/// surfacing a correction flag for a previously evaluated range. Production never references it.
type AcceptanceMarketDataExecutionClient(
    inner: MarketDataExecutionClient,
    gate: AcceptanceScenarioGate) =
    interface IMarketDataExecutionClient with
        member _.VerifyAsync(evidence, ct) = inner.VerifyAsync(evidence, ct)

        member _.LatestCompletedAsync(request, ct) = task {
            let! response = inner.LatestCompletedAsync(request, ct)
            let scenario = gate.View()
            if scenario.Phase = "Running"
               && scenario.ScenarioCode = "evaluated-range-evidence-correction"
               && request.EvaluatedThroughUtc.HasValue then
                return MarketDataExecutionWindowResponse(
                    response.Evidence, response.Bars, true)
            else return response
        }
