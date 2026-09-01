namespace StockTrader.TradingCoreService

open System
open System.Collections.Generic
open System.Text.Json
open StockTrader.Domain.MarketData
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreShadowStore =
    type TradingCoreStore with
        member this.CompareShadowEntry(observation: TradingShadowEntryObservation) =
            let authority = this.Authority()
            use connection = this.Connect()
            let accountGeneration = Int64.Parse(this.StateValue(connection, "account_generation"))
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error(
                observation, authority, accountGeneration, DateTime.UtcNow)) with
            | Some error -> invalidArg "observation" error
            | None ->
                use existing = connection.CreateCommand()
                existing.CommandText <- "SELECT payload_hash,receipt_json FROM shadow_entry_decisions WHERE decision_id=$id"
                existing.Parameters.AddWithValue("$id", observation.DecisionId) |> ignore
                use reader = existing.ExecuteReader()
                if reader.Read() then
                    let storedHash, receiptJson = reader.GetString(0), reader.GetString(1)
                    reader.Close()
                    if storedHash <> observation.PayloadHash then
                        invalidOp "shadow-decision-id-payload-conflict"
                    let receipt = JsonSerializer.Deserialize<TradingShadowDecisionReceipt>(
                        receiptJson, this.Json)
                    if isNull receipt then invalidOp "empty-stored-shadow-receipt"
                    TradingShadowDecisionReceipt(
                        receipt.ContractVersion, receipt.DecisionId, receipt.PayloadHash,
                        receipt.AuthoritativeDisposition, receipt.CandidateDisposition,
                        receipt.CandidateReason, receipt.IsMatch, true, receipt.ComparedAtUtc)
                else
                    reader.Close()
                    let configuration =
                        match this.AccountConfiguration() with
                        | Some value -> value
                        | None -> invalidOp "shadow-account-configuration-missing"
                    let positions = ResizeArray<TradingPositionProjection>()
                    use loadPositions = connection.CreateCommand()
                    loadPositions.CommandText <- "SELECT payload_json FROM projections WHERE kind='position' ORDER BY identity"
                    use positionReader = loadPositions.ExecuteReader()
                    while positionReader.Read() do
                        let value = JsonSerializer.Deserialize<TradingPositionProjection>(
                            positionReader.GetString 0, this.Json)
                        if not (isNull value) then positions.Add value
                    positionReader.Close()
                    use loadRisk = connection.CreateCommand()
                    loadRisk.CommandText <- "SELECT payload_json FROM projections WHERE kind='risk' AND identity='portfolio'"
                    let risk = JsonSerializer.Deserialize<TradingRiskProjection>(
                        Convert.ToString(loadRisk.ExecuteScalar()), this.Json)
                    if isNull risk then invalidOp "shadow-risk-projection-missing"
                    let session = ExchangeSessionPolicy.Evaluate(
                        MarketRegion.UnitedStates, observation.ObservedAtUtc)
                    let candidate = TradingShadowEntryDecisionPolicy.Evaluate(
                        TradingShadowEntryDecisionRequest(
                            observation.OrderMode, observation.Intent, session.IsOpen,
                            risk, positions.ToArray(), configuration))
                    let comparedAt = DateTime.UtcNow
                    let matched = candidate.Disposition = observation.AuthoritativeDisposition
                    let receipt = TradingShadowDecisionReceipt(
                        TradingCoreContractVersions.Current, observation.DecisionId,
                        observation.PayloadHash, observation.AuthoritativeDisposition,
                        candidate.Disposition, candidate.Reason, matched, false, comparedAt)
                    use insert = connection.CreateCommand()
                    insert.CommandText <- "INSERT OR IGNORE INTO shadow_entry_decisions VALUES($id,$hash,$observation,$receipt,$match,$at)"
                    insert.Parameters.AddWithValue("$id", observation.DecisionId) |> ignore
                    insert.Parameters.AddWithValue("$hash", observation.PayloadHash) |> ignore
                    insert.Parameters.AddWithValue("$observation", JsonSerializer.Serialize(observation, this.Json)) |> ignore
                    insert.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    insert.Parameters.AddWithValue("$match", if matched then 1 else 0) |> ignore
                    insert.Parameters.AddWithValue("$at", comparedAt.ToString("O")) |> ignore
                    let inserted = insert.ExecuteNonQuery() = 1
                    if inserted
                        && observation.AuthoritativeDisposition
                            = TradingShadowDispositions.BrokerSubmission then
                        let context = TradingPositionExecutionContext(
                            observation.Intent.ExecutionArtifact,
                            observation.Intent.MarketDataEvidence)
                        use saveContext = connection.CreateCommand()
                        saveContext.CommandText <- "INSERT OR IGNORE INTO shadow_execution_contexts VALUES($signal,$artifact,$context,$decision,$at)"
                        saveContext.Parameters.AddWithValue("$signal", observation.Intent.SourceSignalId) |> ignore
                        saveContext.Parameters.AddWithValue("$artifact", observation.Intent.ExecutionArtifact.ArtifactId) |> ignore
                        saveContext.Parameters.AddWithValue("$context", JsonSerializer.Serialize(context, this.Json)) |> ignore
                        saveContext.Parameters.AddWithValue("$decision", observation.DecisionId) |> ignore
                        saveContext.Parameters.AddWithValue("$at", comparedAt.ToString("O")) |> ignore
                        if saveContext.ExecuteNonQuery() = 0 then
                            use existingContext = connection.CreateCommand()
                            existingContext.CommandText <- "SELECT artifact_id FROM shadow_execution_contexts WHERE source_signal_id=$signal"
                            existingContext.Parameters.AddWithValue("$signal", observation.Intent.SourceSignalId) |> ignore
                            if Convert.ToString(existingContext.ExecuteScalar())
                                <> observation.Intent.ExecutionArtifact.ArtifactId then
                                invalidOp "shadow-source-signal-execution-context-conflict"
                    if inserted then receipt
                    else
                        use raced = connection.CreateCommand()
                        raced.CommandText <- "SELECT payload_hash,receipt_json FROM shadow_entry_decisions WHERE decision_id=$id"
                        raced.Parameters.AddWithValue("$id", observation.DecisionId) |> ignore
                        use racedReader = raced.ExecuteReader()
                        if not (racedReader.Read()) then invalidOp "shadow-decision-concurrent-insert-missing"
                        let storedHash, receiptJson = racedReader.GetString(0), racedReader.GetString(1)
                        if storedHash <> observation.PayloadHash then
                            invalidOp "shadow-decision-id-payload-conflict"
                        let stored = JsonSerializer.Deserialize<TradingShadowDecisionReceipt>(
                            receiptJson, this.Json)
                        if isNull stored then invalidOp "empty-stored-shadow-receipt"
                        TradingShadowDecisionReceipt(
                            stored.ContractVersion, stored.DecisionId, stored.PayloadHash,
                            stored.AuthoritativeDisposition, stored.CandidateDisposition,
                            stored.CandidateReason, stored.IsMatch, true, stored.ComparedAtUtc)

        member this.CompareShadowPosition(observation: TradingShadowPositionObservation) =
            let authority = this.Authority()
            match Option.ofObj (TradingCoreCompatibilityPolicy.Error(
                observation, authority, DateTime.UtcNow)) with
            | Some error -> invalidArg "observation" error
            | None ->
                use connection = this.Connect()
                use loadPosition = connection.CreateCommand()
                loadPosition.CommandText <- "SELECT payload_json FROM projections WHERE kind='position' AND identity=$id"
                loadPosition.Parameters.AddWithValue("$id", observation.PositionId) |> ignore
                let position = JsonSerializer.Deserialize<TradingPositionProjection>(
                    Convert.ToString(loadPosition.ExecuteScalar()), this.Json)
                if isNull position then invalidOp "shadow-position-projection-missing"
                if CanonicalJsonHash.Compute(position) <> observation.PositionStateHash then
                    invalidOp "shadow-position-state-mismatch"
                if isNull position.ExecutionContext
                    || position.ExecutionContext.ExecutionArtifact.ArtifactId
                        <> observation.ExpectedExecutionArtifactId then
                    invalidOp "shadow-position-execution-context-mismatch"
                if position.ExecutionContext.ExecutionArtifact.CalendarVersion
                    <> observation.MarketDataEvidence.CalendarVersion then
                    invalidOp "shadow-position-calendar-mismatch"
                let projectedPolicyState = TradingShadowPositionPolicyState(
                    position.HighSinceEntry, position.StopLossPrice,
                    position.InitialRiskDistance, position.BreakevenApplied,
                    position.TrailingStopActivated)
                if projectedPolicyState <> observation.AuthoritativePolicyState then
                    invalidOp "shadow-position-authoritative-policy-state-mismatch"
                let validateQuantity (disposition: string) (action: string) (quantity: Nullable<int>) =
                    if disposition = TradingShadowDispositions.PositionCommand
                        && action <> TradingPositionActionKinds.ScaleIn
                        && quantity.Value > position.Quantity then
                        invalidOp "shadow-position-quantity-exceeds-open-position"
                validateQuantity observation.AuthoritativeDisposition
                    observation.AuthoritativeAction observation.AuthoritativeQuantity
                validateQuantity observation.CandidateDisposition
                    observation.CandidateAction observation.CandidateQuantity
                use existing = connection.CreateCommand()
                existing.CommandText <- "SELECT payload_hash,receipt_json FROM shadow_position_decisions WHERE decision_id=$id"
                existing.Parameters.AddWithValue("$id", observation.DecisionId) |> ignore
                use reader = existing.ExecuteReader()
                if reader.Read() then
                    let storedHash, receiptJson = reader.GetString(0), reader.GetString(1)
                    reader.Close()
                    if storedHash <> observation.PayloadHash then
                        invalidOp "shadow-position-decision-id-payload-conflict"
                    let stored = JsonSerializer.Deserialize<TradingShadowPositionDecisionReceipt>(
                        receiptJson, this.Json)
                    if isNull stored then invalidOp "empty-stored-shadow-position-receipt"
                    TradingShadowPositionDecisionReceipt(
                        stored.ContractVersion, stored.DecisionId, stored.PayloadHash,
                        stored.AuthoritativeDisposition, stored.CandidateDisposition,
                        stored.AuthoritativeAction, stored.CandidateAction,
                        stored.AuthoritativeQuantity, stored.CandidateQuantity,
                        stored.IsPolicyStateMatch, stored.IsMatch, true, stored.ComparedAtUtc)
                else
                    reader.Close()
                    let decisionMatched =
                        observation.AuthoritativeDisposition = observation.CandidateDisposition
                        && observation.AuthoritativeAction = observation.CandidateAction
                        && observation.AuthoritativeQuantity = observation.CandidateQuantity
                    let policyStateMatched =
                        observation.AuthoritativePolicyState = observation.CandidatePolicyState
                    let matched = decisionMatched && policyStateMatched
                    let comparedAt = DateTime.UtcNow
                    let receipt = TradingShadowPositionDecisionReceipt(
                        TradingCoreContractVersions.Current, observation.DecisionId,
                        observation.PayloadHash, observation.AuthoritativeDisposition,
                        observation.CandidateDisposition, observation.AuthoritativeAction,
                        observation.CandidateAction, observation.AuthoritativeQuantity,
                        observation.CandidateQuantity, policyStateMatched, matched, false, comparedAt)
                    use insert = connection.CreateCommand()
                    insert.CommandText <- "INSERT OR IGNORE INTO shadow_position_decisions VALUES($id,$hash,$observation,$receipt,$match,$at)"
                    insert.Parameters.AddWithValue("$id", observation.DecisionId) |> ignore
                    insert.Parameters.AddWithValue("$hash", observation.PayloadHash) |> ignore
                    insert.Parameters.AddWithValue("$observation", JsonSerializer.Serialize(observation, this.Json)) |> ignore
                    insert.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    insert.Parameters.AddWithValue("$match", if matched then 1 else 0) |> ignore
                    insert.Parameters.AddWithValue("$at", comparedAt.ToString("O")) |> ignore
                    if insert.ExecuteNonQuery() = 1 then receipt
                    else
                        use raced = connection.CreateCommand()
                        raced.CommandText <- "SELECT payload_hash,receipt_json FROM shadow_position_decisions WHERE decision_id=$id"
                        raced.Parameters.AddWithValue("$id", observation.DecisionId) |> ignore
                        use racedReader = raced.ExecuteReader()
                        if not (racedReader.Read()) then invalidOp "shadow-position-concurrent-insert-missing"
                        if racedReader.GetString(0) <> observation.PayloadHash then
                            invalidOp "shadow-position-decision-id-payload-conflict"
                        let stored = JsonSerializer.Deserialize<TradingShadowPositionDecisionReceipt>(
                            racedReader.GetString(1), this.Json)
                        if isNull stored then invalidOp "empty-stored-shadow-position-receipt"
                        TradingShadowPositionDecisionReceipt(
                            stored.ContractVersion, stored.DecisionId, stored.PayloadHash,
                            stored.AuthoritativeDisposition, stored.CandidateDisposition,
                            stored.AuthoritativeAction, stored.CandidateAction,
                            stored.AuthoritativeQuantity, stored.CandidateQuantity,
                            stored.IsPolicyStateMatch, stored.IsMatch, true, stored.ComparedAtUtc)

        member this.ShadowSummary() =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT COUNT(*),COALESCE(SUM(is_match),0),MAX(compared_at) FROM (SELECT is_match,compared_at FROM shadow_entry_decisions UNION ALL SELECT is_match,compared_at FROM shadow_position_decisions)"
            use reader = command.ExecuteReader()
            if not (reader.Read()) then invalidOp "shadow-summary-unavailable"
            let total, matched = reader.GetInt64 0, reader.GetInt64 1
            let last =
                if reader.IsDBNull 2 then Nullable()
                else Nullable(DateTime.Parse(reader.GetString 2, null,
                    Globalization.DateTimeStyles.RoundtripKind))
            TradingShadowSummary(TradingCoreContractVersions.Current,
                total, matched, total - matched, last)
