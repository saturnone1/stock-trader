namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open StockTrader.ServiceContracts.MarketData
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Execution

/// Trading Core independently protects open capital from completed, persisted market-data bars.
/// Edge is not involved in evaluation, command creation, broker submission, or reconciliation.
type PositionProtectionWorker(
    store: TradingCoreStore,
    marketData: MarketDataExecutionClient,
    config: ServiceConfig,
    logger: ILogger<PositionProtectionWorker>) =
    inherit BackgroundService()

    let identity (values: string seq) =
        values
        |> String.concat "|"
        |> Encoding.UTF8.GetBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> fun value -> value.ToLowerInvariant()

    let pending positionId =
        match store.LatestPositionCommand positionId with
        | Some value ->
            value.Status = TradingCommandStatuses.PendingBrokerSubmission
            || value.Status = TradingCommandStatuses.AwaitingBrokerEvidence
            || value.Status = TradingCommandStatuses.ReconciliationRequired
        | None -> false

    let window (evidence: MarketDataEvidenceContract) symbol required
        (completed: TradingCompletedBarWindow) afterRevision evaluatedThrough =
        MarketDataExecutionWindowRequest(
            MarketDataContractVersions.Current, evidence.Provider, symbol,
            evidence.TimeFrame, evidence.AdjustmentMode, evidence.Market,
            evidence.CalendarVersion,
            completed.CompletedThroughUtc.AddDays(
                -float (MarketDataExecutionEvidenceLimits.RequiredDailyLookbackCalendarDays required)),
            completed.CompletedThroughUtc, required, completed.ExpectedLastSessionDate,
            afterRevision, evaluatedThrough)

    let envelopeWithHash (value: TradingCommandEnvelope) payload =
        TradingCommandEnvelope(
            value.ContractVersion, value.CommandId, value.CommandKind, payload,
            value.CorrelationId, value.CausationId, value.AuthorityGeneration,
            value.AccountGeneration, value.OccurredAtUtc, value.ExpiresAtUtc)

    let stateUpdate (status: TradingCoreStatus) (position: TradingPositionProjection)
        (evaluation: TradingPositionEvaluation) (evidence: MarketDataEvidenceContract)
        evaluationRevision (now: DateTime) =
        let artifact = position.ExecutionContext.ExecutionArtifact
        let commandId = "position-state:" + identity [
            string status.AuthorityGeneration; position.PositionId; artifact.ArtifactId
            evidence.EvidenceId; string evaluation.HighSinceEntry
            string evaluation.StopLossPrice; string evaluation.InitialRiskDistance
            evaluation.EvaluatedThroughBarUtc.ToString("O"); string evaluationRevision ]
        let envelope = TradingCommandEnvelope(
            TradingCoreContractVersions.Current, commandId,
            TradingCommandKinds.UpdatePositionState, "", commandId,
            position.SourceSignalId, status.AuthorityGeneration,
            status.AccountGeneration, now, now.AddMinutes 5)
        let update = TradingPositionPolicyStateUpdate(
            envelope, position.PositionId, artifact.ArtifactId,
            evaluation.HighSinceEntry, evaluation.StopLossPrice,
            evaluation.InitialRiskDistance, evaluation.BreakevenApplied,
            evaluation.TrailingStopActivated, evidence, evaluation.EntryAtr,
            evaluation.EvaluatedThroughBarUtc, evaluationRevision)
        let payload = TradingCoreIdentity.PositionStatePayload update
        TradingPositionPolicyStateUpdate(envelopeWithHash update.Envelope payload,
            update.PositionId, update.ExpectedExecutionArtifactId,
            update.HighSinceEntry, update.StopLossPrice, update.InitialRiskDistance,
            update.BreakevenApplied, update.TrailingStopActivated,
            update.MarketDataEvidence, update.EntryAtr,
            evaluation.EvaluatedThroughBarUtc, update.EvaluatedMarketDataRevision)

    let positionCommand (status: TradingCoreStatus) (position: TradingPositionProjection)
        (evaluation: TradingPositionEvaluation) (evidence: MarketDataEvidenceContract)
        evaluationRevision (now: DateTime) =
        let artifact = position.ExecutionContext.ExecutionArtifact
        let commandId = "position:" + identity [
            string status.AuthorityGeneration; position.PositionId; evaluation.Action
            string evaluation.Quantity; evaluation.Reason; artifact.ArtifactId
            evidence.EvidenceId; string evaluation.ScalingRuleIndex
            evaluation.EvaluatedThroughBarUtc.ToString("O"); string evaluationRevision ]
        let envelope = TradingCommandEnvelope(
            TradingCoreContractVersions.Current, commandId,
            TradingCommandKinds.ClosePosition, "", commandId,
            position.SourceSignalId, status.AuthorityGeneration,
            status.AccountGeneration, now, now.AddMinutes 5)
        let policyState = TradingShadowPositionPolicyState(
            evaluation.HighSinceEntry, evaluation.StopLossPrice,
            evaluation.InitialRiskDistance, evaluation.BreakevenApplied,
            evaluation.TrailingStopActivated)
        let command = TradingPositionCommand(
            envelope, position.PositionId, evaluation.Action,
            evaluation.Quantity, evaluation.Reason, artifact.ArtifactId, evidence,
            evaluation.ScalingRuleIndex,
            evaluation.MarksPartialProfit, policyState, evaluation.EntryAtr,
            evaluation.EvaluatedThroughBarUtc, evaluationRevision)
        let payload = TradingCoreIdentity.PositionPayload command
        TradingPositionCommand(envelopeWithHash command.Envelope payload,
            command.PositionId, command.Action, command.Quantity, command.Reason,
            command.ExpectedExecutionArtifactId, command.MarketDataEvidence,
            command.ScalingRuleIndex, command.MarksPartialProfit,
            command.EvaluatedPolicyState, command.EvaluatedEntryAtr,
            command.EvaluatedThroughBarUtc, command.EvaluatedMarketDataRevision)

    let evaluatePosition (status: TradingCoreStatus) (portfolio: TradingCorePortfolioView)
        (configuration: TradingAccountConfigurationSet)
        (position: TradingPositionProjection) (ct: CancellationToken) = task {
        if isNull position.ExecutionContext || isNull position.ExecutionContext.ExecutionArtifact.PositionManagement then
            logger.LogWarning(
                "Position {PositionId} has a legacy artifact; autonomous protection is fail-closed",
                position.PositionId)
        elif not (pending position.PositionId) then
            let artifact = position.ExecutionContext.ExecutionArtifact
            let management = artifact.PositionManagement
            let now = DateTime.UtcNow
            let completed = TradingCompletedBarPolicy.Resolve(
                now, position.ExecutionContext.EntryMarketDataEvidence.Provider)
            let references = Dictionary<string, MarketDataExecutionWindowResponse>(StringComparer.OrdinalIgnoreCase)
            for symbol in TradingPositionEvaluator.ReferenceSymbols artifact do
                if not (symbol.Equals(position.Symbol, StringComparison.OrdinalIgnoreCase)) then
                    let! series = marketData.LatestCompletedAsync(
                        window position.ExecutionContext.EntryMarketDataEvidence
                            symbol management.RequiredBars completed
                            position.LastEvaluatedMarketDataRevision
                            position.LastEvaluatedBarUtc, ct)
                    if series.PriorEvaluatedRangeCorrected then
                        invalidOp "reference-market-data-correction-requires-reconciliation"
                    references[symbol] <- series
            // Fetch primary last so its global revision is at least every reference response revision.
            let! primary = marketData.LatestCompletedAsync(
                window position.ExecutionContext.EntryMarketDataEvidence
                    position.Symbol management.RequiredBars completed
                    position.LastEvaluatedMarketDataRevision position.LastEvaluatedBarUtc, ct)
            if primary.PriorEvaluatedRangeCorrected then
                invalidOp "position-market-data-correction-requires-reconciliation"
            let evaluationRevision =
                references.Values
                |> Seq.map _.Evidence.Revision
                |> Seq.append (Seq.singleton primary.Evidence.Revision)
                |> Seq.min
            if not position.LastEvaluatedBarUtc.HasValue
                || position.LastEvaluatedBarUtc.Value < primary.Evidence.LastBarUtc.Value then
                let account = portfolio.Accounts |> Seq.tryFind (fun value -> value.AccountId = position.AccountId)
                let equity = account |> Option.map _.TotalEquity |> Option.defaultValue 0m
                let evaluation = TradingPositionEvaluator.Evaluate(
                    position, primary, references, equity,
                    configuration.Risk.MaxTotalPositions)
                if String.IsNullOrWhiteSpace evaluation.Action then
                    store.ApplyPositionState(
                        stateUpdate status position evaluation primary.Evidence
                            evaluationRevision now) |> ignore
                else
                    store.AcceptPosition(
                        positionCommand status position evaluation primary.Evidence
                            evaluationRevision now) |> ignore
                    logger.LogInformation(
                        "Autonomous position decision {PositionId}/{Symbol}: {Action} {Quantity}",
                        position.PositionId, position.Symbol, evaluation.Action, evaluation.Quantity)
    }

    override _.ExecuteAsync(stoppingToken: CancellationToken) = task {
        try
            while not stoppingToken.IsCancellationRequested do
                try
                    let status = store.Status()
                    match store.AccountConfiguration() with
                    | Some configuration when status.Mode = TradingAuthorityMode.Remote && status.Ready ->
                        let portfolio = store.Portfolio()
                        for position in portfolio.Positions do
                            if not position.ClosedAtUtc.HasValue then
                                do! evaluatePosition status portfolio configuration position stoppingToken
                    | _ -> ()
                with
                | :? OperationCanceledException when stoppingToken.IsCancellationRequested -> ()
                | error -> logger.LogError(error, "Autonomous position protection cycle failed")
                do! Task.Delay(config.PositionEvaluationInterval, stoppingToken)
        with :? OperationCanceledException when stoppingToken.IsCancellationRequested -> ()
    }
