namespace StockTrader.TradingCoreService

open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker

/// C#-visible facade over the small F# persistence modules. HTTP and broker adapters use the same
/// operations directly; this facade keeps cross-language integration tests strongly typed.
type TradingCoreOperations(store: TradingCoreStore) =
    member _.Authority() = store.Authority()
    member _.AuthorityV2() = store.AuthorityV2()
    member _.CreateTransition(request) = store.CreateTransition request
    member _.ApplyTransitionStep(request) = store.ApplyTransitionStep request
    member _.Transition(transitionId) = store.Transition transitionId
    member _.ImportFinancialTransfer(transfer) = store.ImportFinancialTransfer transfer
    member _.Import(snapshot) = store.Import snapshot
    member _.ApplyAccountConfiguration(configuration) = store.ApplyAccountConfiguration configuration
    member _.Activate(authority) = store.Activate authority
    member _.Status() = store.Status()
    member _.Portfolio() = store.Portfolio()
    member _.AcceptEntry(intent) = store.AcceptEntry intent
    member _.RecordRecommendation(observation) = store.RecordRecommendation observation
    member _.CompareShadowEntry(observation) = store.CompareShadowEntry observation
    member _.CompareShadowPosition(observation) = store.CompareShadowPosition observation
    member _.ShadowSummary() = store.ShadowSummary()
    member _.ClaimEntry() = store.ClaimEntry()
    member _.RejectExpiredPendingIntents(observedAtUtc) =
        store.RejectExpiredPendingIntents observedAtUtc
    member _.RecordBrokerEvidence(commandId, evidence: BrokerOrderEvidence) =
        store.RecordBrokerEvidence(commandId, evidence)
    member _.AcceptPosition(command) = store.AcceptPosition command
    member _.ApplyPositionState(update) = store.ApplyPositionState update
    member _.ClaimPosition() = store.ClaimPosition()
    member _.RecordPositionBrokerEvidence(commandId, evidence: BrokerOrderEvidence) =
        store.RecordPositionBrokerEvidence(commandId, evidence)
    member _.CommandStatus(commandId) = store.CommandStatus commandId
    member _.LatestPositionCommand(positionId) = store.LatestPositionCommand positionId
    member _.LatestEntryCommand(sourceSignalId) = store.LatestEntryCommand sourceSignalId
    member _.SyncBrokerPortfolio(accountId, account, positions, dailyLossLimitPercent) =
        store.SyncBrokerPortfolio(accountId, account, positions, dailyLossLimitPercent)
    member _.RejectIntent(commandId, reason) = store.RejectIntent(commandId, reason)
