namespace StockTrader.TradingCoreService

open System
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker

type BrokerWorker(store: TradingCoreStore, logger: ILogger<BrokerWorker>) =
    inherit BackgroundService()

    let clientOrderId (commandId: string) =
        let input: byte array = Encoding.UTF8.GetBytes(commandId)
        let hash: byte array = SHA256.HashData(input)
        "st-" + Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 32)

    let brokerFor (configuration: TradingAccountConfigurationSet) accountId =
        let account = configuration.Accounts |> Seq.tryFind (fun item ->
            item.AccountId = accountId && item.IsEnabled && item.IsActive)
        match account with
        | None -> Error "active-account-configuration-missing"
        | Some value when value.BrokerCode <> "Alpaca" -> Error "unsupported-trading-core-broker"
        | Some value -> Ok (new AlpacaTradingBroker(
            value.ApiKey, value.ApiSecret,
            not (String.Equals(value.Environment, "Live", StringComparison.OrdinalIgnoreCase)),
            TimeProvider.System))

    let reconcile (broker: ITradingBroker) clientId ct = task {
        let! orders = broker.GetOrdersAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, ct)
        return orders |> Seq.tryFind (fun order -> order.ClientOrderId = clientId)
    }

    override _.ExecuteAsync(stoppingToken: CancellationToken) = task {
        try
            while not stoppingToken.IsCancellationRequested do
                let unresolved = store.UnresolvedEntry()
                let intent, shouldSubmit =
                    match unresolved with
                    | Some value -> Some value, false
                    | None -> store.ClaimEntry(), true
                match intent, store.AccountConfiguration() with
                | Some intent, Some configuration ->
                    match brokerFor configuration intent.AccountId with
                    | Error reason ->
                        store.RequireReconciliation intent.Envelope.CommandId
                        logger.LogError("Trading entry {CommandId} blocked: {Reason}",
                            intent.Envelope.CommandId, reason)
                    | Ok broker ->
                        use broker = broker
                        let clientId = clientOrderId intent.Envelope.CommandId
                        if not shouldSubmit then
                            try
                                let! evidence = reconcile broker clientId stoppingToken
                                match evidence with
                                | Some order -> store.RecordBrokerEvidence(intent.Envelope.CommandId, order) |> ignore
                                | None -> do! Task.Delay(5000, stoppingToken)
                            with error ->
                                store.RequireReconciliation intent.Envelope.CommandId
                                logger.LogError(error, "Trading entry {CommandId} reconciliation failed",
                                    intent.Envelope.CommandId)
                                do! Task.Delay(5000, stoppingToken)
                        else
                            try
                                let! account = broker.GetAccountAsync(stoppingToken)
                                let! positions = broker.GetPositionsAsync(stoppingToken)
                                let decision = TradingRiskGate.Evaluate(TradingRiskGateRequest(
                                    configuration.Risk.DailyLossLimitPercent,
                                    configuration.Risk.MaxTotalPositions,
                                    configuration.Risk.MaxPositionsPerSector,
                                    intent.Symbol, intent.Sector, account, positions,
                                    store.PositionRiskEvidence()))
                                if not decision.Allowed then
                                    store.RejectIntent(intent.Envelope.CommandId, decision.Reason)
                                    logger.LogWarning("Trading entry {CommandId} rejected by final risk gate: {Reason}",
                                        intent.Envelope.CommandId, decision.Reason)
                                else
                                    let request = BrokerEntryOrderRequest(clientId, intent.Symbol,
                                        intent.ShareQuantity, intent.TargetPrice, intent.StopLossPrice)
                                    let! evidence = broker.SubmitEntryAsync(request, stoppingToken)
                                    store.RecordBrokerEvidence(intent.Envelope.CommandId, evidence) |> ignore
                                    logger.LogInformation("Trading entry {CommandId} has broker evidence {OrderId}/{Status}",
                                        intent.Envelope.CommandId, evidence.OrderId, evidence.Status)
                                    if evidence.Status <> "Filled" && evidence.Status <> "Rejected"
                                        && evidence.Status <> "Cancelled" && evidence.Status <> "Expired" then
                                        do! Task.Delay(2000, stoppingToken)
                            with error ->
                                try
                                    let! evidence = reconcile broker clientId stoppingToken
                                    match evidence with
                                    | Some order -> store.RecordBrokerEvidence(intent.Envelope.CommandId, order) |> ignore
                                    | None -> store.RequireReconciliation intent.Envelope.CommandId
                                with reconcileError ->
                                    store.RequireReconciliation intent.Envelope.CommandId
                                    logger.LogError(reconcileError,
                                        "Trading entry {CommandId} reconciliation failed after submission error",
                                        intent.Envelope.CommandId)
                                logger.LogWarning(error,
                                    "Trading entry {CommandId} submission did not return durable evidence",
                                    intent.Envelope.CommandId)
                | Some intent, None ->
                    store.RequireReconciliation intent.Envelope.CommandId
                    logger.LogError("Trading entry {CommandId} blocked: account configuration unavailable",
                        intent.Envelope.CommandId)
                | None, _ -> do! Task.Delay(500, stoppingToken)
        with :? OperationCanceledException when stoppingToken.IsCancellationRequested -> ()
    }
