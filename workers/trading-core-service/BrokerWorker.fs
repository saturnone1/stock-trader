namespace StockTrader.TradingCoreService

open System
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.Domain.MarketData
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker

type BrokerWorker(store: TradingCoreStore, logger: ILogger<BrokerWorker>) =
    inherit BackgroundService()

    let mutable nextPortfolioSync = DateTime.MinValue

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

    let preflight (broker: ITradingBroker)
        (configuration: TradingAccountConfigurationSet)
        (intent: TradingEntryIntent)
        (ct: CancellationToken) = task {
        try
            if not (store.FinancialStateReady()) then
                invalidOp "trading-core-financial-state-not-reconciled"
            let! account = broker.GetAccountAsync(ct)
            let! positions = broker.GetPositionsAsync(ct)
            return Ok (TradingRiskGate.Evaluate(TradingRiskGateRequest(
                configuration.Risk.DailyLossLimitPercent,
                configuration.Risk.MaxTotalPositions,
                configuration.Risk.MaxPositionsPerSector,
                intent.Symbol, intent.Sector, account, positions,
                store.PositionRiskEvidence())))
        with error -> return Error error
    }

    let syncPortfolio (configuration: TradingAccountConfigurationSet) ct = task {
        if DateTime.UtcNow >= nextPortfolioSync then
            nextPortfolioSync <- DateTime.UtcNow.AddSeconds(5)
            for account in configuration.Accounts do
                if account.IsEnabled && account.IsActive then
                    match brokerFor configuration account.AccountId with
                    | Error reason ->
                        logger.LogError("Trading portfolio sync for account {AccountId} blocked: {Reason}",
                            account.AccountId, reason)
                    | Ok broker ->
                        use broker = broker
                        try
                            let! accountEvidence = broker.GetAccountAsync(ct)
                            let! positions = broker.GetPositionsAsync(ct)
                            store.SyncBrokerPortfolio(account.AccountId, accountEvidence, positions,
                                configuration.Risk.DailyLossLimitPercent)
                        with error ->
                            logger.LogWarning(error,
                                "Trading portfolio sync for account {AccountId} failed",
                                account.AccountId)
    }

    override _.ExecuteAsync(stoppingToken: CancellationToken) = task {
        try
            while not stoppingToken.IsCancellationRequested do
                let expired = store.RejectExpiredPendingIntents(DateTime.UtcNow)
                if expired > 0 then
                    logger.LogWarning(
                        "Rejected {Count} trading commands that expired before broker submission", expired)
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
                            let session = ExchangeSessionPolicy.Evaluate(
                                MarketRegion.UnitedStates, DateTime.UtcNow)
                            if not session.IsOpen then
                                store.RejectIntent(intent.Envelope.CommandId, session.Reason)
                                logger.LogWarning(
                                    "Trading entry {CommandId} rejected outside regular session: {Reason}",
                                    intent.Envelope.CommandId, session.Reason)
                            else
                                let! readiness = preflight broker configuration intent stoppingToken
                                match readiness with
                                | Error error ->
                                    store.ReleaseEntryForRetry(intent.Envelope.CommandId) |> ignore
                                    logger.LogWarning(error,
                                        "Trading entry {CommandId} pre-submit evidence unavailable; released for retry",
                                        intent.Envelope.CommandId)
                                    do! Task.Delay(2000, stoppingToken)
                                | Ok decision when not decision.Allowed ->
                                        store.RejectIntent(intent.Envelope.CommandId, decision.Reason)
                                        logger.LogWarning("Trading entry {CommandId} rejected by final risk gate: {Reason}",
                                            intent.Envelope.CommandId, decision.Reason)
                                | Ok _ ->
                                    try
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
                | None, _ -> ()

                let unresolvedPosition = store.UnresolvedPosition()
                let positionCommand, shouldSubmitPosition =
                    match unresolvedPosition with
                    | Some value -> Some value, false
                    | None -> store.ClaimPosition(), true
                match positionCommand, store.AccountConfiguration() with
                | Some command, Some configuration ->
                    match store.LoadPosition command.PositionId with
                    | None -> store.RejectIntent(command.Envelope.CommandId, "open-position-not-found")
                    | Some position ->
                        match brokerFor configuration position.AccountId with
                        | Error reason ->
                            store.RequireReconciliation command.Envelope.CommandId
                            logger.LogError("Trading position command {CommandId} blocked: {Reason}",
                                command.Envelope.CommandId, reason)
                        | Ok broker ->
                            use broker = broker
                            let clientId = clientOrderId command.Envelope.CommandId
                            if not shouldSubmitPosition then
                                try
                                    let! evidence = reconcile broker clientId stoppingToken
                                    match evidence with
                                    | Some order -> store.RecordPositionBrokerEvidence(
                                        command.Envelope.CommandId, order) |> ignore
                                    | None -> ()
                                with error ->
                                    store.RequireReconciliation command.Envelope.CommandId
                                    logger.LogError(error,
                                        "Trading position command {CommandId} reconciliation failed",
                                        command.Envelope.CommandId)
                            else
                                try
                                    let! brokerPositions = broker.GetPositionsAsync(stoppingToken)
                                    let brokerPosition = brokerPositions |> Seq.tryFind (fun value ->
                                        value.Symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase))
                                    let valid =
                                        match command.Action, brokerPosition with
                                        | action, Some current when action = TradingPositionActionKinds.ScaleIn ->
                                            current.Quantity = position.Quantity
                                        | _, Some current ->
                                            (current.Quantity = position.Quantity)
                                            && (command.Quantity <= current.Quantity)
                                        | _ -> false
                                    if not valid then
                                        store.RequireReconciliation(command.Envelope.CommandId)
                                        logger.LogError(
                                            "Trading position command {CommandId} broker quantity mismatch",
                                            command.Envelope.CommandId)
                                    else
                                        let request = BrokerPositionOrderRequest(
                                            clientId, position.Symbol, command.Quantity)
                                        let! evidence =
                                            if command.Action = TradingPositionActionKinds.ScaleIn then
                                                broker.IncreasePositionAsync(request, stoppingToken)
                                            else broker.ClosePositionAsync(request, stoppingToken)
                                        store.RecordPositionBrokerEvidence(
                                            command.Envelope.CommandId, evidence) |> ignore
                                with error ->
                                    try
                                        let! evidence = reconcile broker clientId stoppingToken
                                        match evidence with
                                        | Some order -> store.RecordPositionBrokerEvidence(
                                            command.Envelope.CommandId, order) |> ignore
                                        | None -> store.RequireReconciliation command.Envelope.CommandId
                                    with reconcileError ->
                                        store.RequireReconciliation command.Envelope.CommandId
                                        logger.LogError(reconcileError,
                                            "Trading position command {CommandId} reconciliation failed after submission error",
                                            command.Envelope.CommandId)
                                    logger.LogWarning(error,
                                        "Trading position command {CommandId} submission has ambiguous result",
                                        command.Envelope.CommandId)
                | Some command, None ->
                    store.RequireReconciliation command.Envelope.CommandId
                    logger.LogError("Trading position command {CommandId} blocked: account configuration unavailable",
                        command.Envelope.CommandId)
                | None, _ -> ()
                match store.AccountConfiguration() with
                | Some configuration when store.Authority().Mode = TradingAuthorityMode.Remote ->
                    do! syncPortfolio configuration stoppingToken
                | _ -> ()
                do! Task.Delay(500, stoppingToken)
        with :? OperationCanceledException when stoppingToken.IsCancellationRequested -> ()
    }
