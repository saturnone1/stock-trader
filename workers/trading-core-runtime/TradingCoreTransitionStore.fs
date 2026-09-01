namespace StockTrader.TradingCoreService

open System
open System.Text.Json
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore

[<AutoOpen>]
module TradingCoreTransitionStore =
    let private utc (value: DateTime) = value.ToUniversalTime().ToString("O")

    let private readTransition (store: TradingCoreStore) (reader: SqliteDataReader) =
        let value = JsonSerializer.Deserialize<AuthorityTransitionView>(reader.GetString 0, store.Json)
        if isNull value then invalidOp "empty-authority-transition"
        value

    let private operationReceipt (store: TradingCoreStore) (connection: SqliteConnection)
        (operation: TradingControlOperation) =
        use command = connection.CreateCommand()
        command.CommandText <- "SELECT payload_hash,receipt_json FROM authority_transition_operations WHERE operation_id=$id"
        command.Parameters.AddWithValue("$id", operation.OperationId) |> ignore
        use reader = command.ExecuteReader()
        if not (reader.Read()) then None
        elif reader.GetString 0 <> operation.PayloadHash then
            invalidOp "operation-identity-conflict"
        else
            let receipt = JsonSerializer.Deserialize<AuthorityTransitionReceipt>(reader.GetString 1, store.Json)
            if isNull receipt then invalidOp "empty-authority-transition-receipt"
            Some (AuthorityTransitionReceipt(
                receipt.ContractVersion, receipt.OperationId, receipt.PayloadHash,
                receipt.TransitionId, receipt.Phase, receipt.Outcome,
                receipt.EffectiveGeneration, true, receipt.RecordedAtUtc))

    let private persist (store: TradingCoreStore) (connection: SqliteConnection)
        (transaction: SqliteTransaction) (view: AuthorityTransitionView)
        (operation: TradingControlOperation) =
        use transition = connection.CreateCommand()
        transition.Transaction <- transaction
        transition.CommandText <- """INSERT INTO authority_transitions
(transition_id,phase,outcome,source_generation,reserved_generation,payload_json,updated_at)
VALUES($id,$phase,$outcome,$source,$reserved,$payload,$at)
ON CONFLICT(transition_id) DO UPDATE SET phase=excluded.phase,outcome=excluded.outcome,
payload_json=excluded.payload_json,updated_at=excluded.updated_at"""
        transition.Parameters.AddWithValue("$id", view.TransitionId) |> ignore
        transition.Parameters.AddWithValue("$phase", view.Phase) |> ignore
        transition.Parameters.AddWithValue("$outcome", view.Outcome) |> ignore
        transition.Parameters.AddWithValue("$source", view.SourceGeneration) |> ignore
        transition.Parameters.AddWithValue("$reserved", view.ReservedGeneration) |> ignore
        transition.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(view, store.Json)) |> ignore
        transition.Parameters.AddWithValue("$at", utc operation.ObservedAtUtc) |> ignore
        transition.ExecuteNonQuery() |> ignore
        let effectiveGeneration =
            if view.Outcome = AuthorityTransitionOutcomes.None then view.SourceGeneration
            else view.ReservedGeneration
        let receipt = AuthorityTransitionReceipt(
            TradingControlContractVersions.Current, operation.OperationId,
            operation.PayloadHash, view.TransitionId, view.Phase, view.Outcome,
            effectiveGeneration, false, operation.ObservedAtUtc)
        use operationCommand = connection.CreateCommand()
        operationCommand.Transaction <- transaction
        operationCommand.CommandText <- """INSERT INTO authority_transition_operations
(operation_id,transition_id,payload_hash,receipt_json,recorded_at)
VALUES($operation,$transition,$hash,$receipt,$at)"""
        operationCommand.Parameters.AddWithValue("$operation", operation.OperationId) |> ignore
        operationCommand.Parameters.AddWithValue("$transition", view.TransitionId) |> ignore
        operationCommand.Parameters.AddWithValue("$hash", operation.PayloadHash) |> ignore
        operationCommand.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, store.Json)) |> ignore
        operationCommand.Parameters.AddWithValue("$at", utc operation.ObservedAtUtc) |> ignore
        operationCommand.ExecuteNonQuery() |> ignore
        receipt

    let private requireFence (receipt: AuthorityFenceReceipt) =
        if isNull receipt
            || receipt.NewEntryAcceptance <> AuthorityCommandAcceptanceStates.Fenced
            || receipt.ManualCommandAcceptance <> AuthorityCommandAcceptanceStates.Fenced
            || receipt.AuthorityGeneration < 1L
            || receipt.FenceHash <> TradingControlIdentity.Fence(receipt) then
            invalidOp "command-fence-not-proven"

    type TradingCoreStore with
        member this.AuthorityV2() =
            let authority = this.Authority()
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM authority_transitions WHERE phase <> 'Completed' ORDER BY reserved_generation DESC LIMIT 1"
            match command.ExecuteScalar() with
            | null -> TradingAuthorityV2View(
                TradingControlContractVersions.Current, authority.Mode,
                AuthorityOwners.ForMode authority.Mode, authority.Generation,
                AuthorityCommandAcceptanceStates.Open, null, null)
            | payload ->
                let transition = JsonSerializer.Deserialize<AuthorityTransitionView>(Convert.ToString payload, this.Json)
                if isNull transition then invalidOp "empty-authority-transition"
                TradingAuthorityV2View(
                    TradingControlContractVersions.Current, authority.Mode,
                    AuthorityOwners.ForMode authority.Mode, authority.Generation,
                    AuthorityCommandAcceptanceStates.Fenced,
                    transition.TransitionId, transition.Phase)

        member this.RequireCommandAcceptance() =
            if this.AuthorityV2().CommandAcceptance
                <> AuthorityCommandAcceptanceStates.Open then
                invalidOp "authority-command-acceptance-fenced"

        member this.DrainInventory(transitionId: string) =
            let transition: AuthorityTransitionView =
                match this.Transition transitionId with
                | Some value -> value
                | None -> invalidOp "authority-transition-not-found"
            if transition.Phase <> AuthorityTransitionPhases.Quiescing
                && transition.Phase <> AuthorityTransitionPhases.Draining then
                invalidOp "authority-transition-phase-conflict"
            use connection = this.Connect()
            use unresolved = connection.CreateCommand()
            unresolved.CommandText <- """SELECT COUNT(*) FROM financial_intents
WHERE status IN ($pending,$awaiting,$reconcile)"""
            unresolved.Parameters.AddWithValue("$pending", TradingCommandStatuses.PendingBrokerSubmission) |> ignore
            unresolved.Parameters.AddWithValue("$awaiting", TradingCommandStatuses.AwaitingBrokerEvidence) |> ignore
            unresolved.Parameters.AddWithValue("$reconcile", TradingCommandStatuses.ReconciliationRequired) |> ignore
            let unresolvedCount = Convert.ToInt32(unresolved.ExecuteScalar())
            use corrections = connection.CreateCommand()
            corrections.CommandText <- "SELECT COUNT(*) FROM state WHERE key LIKE 'position_evidence_correction:%'"
            let correctionCount = Convert.ToInt32(corrections.ExecuteScalar())
            use journal = connection.CreateCommand()
            journal.CommandText <- "SELECT COUNT(*) FROM outbox"
            let journalCount = Convert.ToInt64(journal.ExecuteScalar())
            use lastBar = connection.CreateCommand()
            lastBar.CommandText <- "SELECT MAX(json_extract(payload_json,'$.lastEvaluatedBarUtc')) FROM canonical_positions"
            let lastCompleted =
                match lastBar.ExecuteScalar() with
                | null -> Nullable()
                | value when String.IsNullOrWhiteSpace(Convert.ToString value) -> Nullable()
                | value -> Nullable(DateTime.Parse(Convert.ToString value, null,
                    Globalization.DateTimeStyles.RoundtripKind))
            let candidate = AuthorityDrainInventory(
                unresolvedCount + correctionCount, unresolvedCount + correctionCount, 0,
                journalCount, 0L,
                this.UtcNow, "")
            AuthorityDrainInventory(
                candidate.UnresolvedIntentCount, candidate.UnresolvedBrokerEffectCount,
                candidate.UnprocessedBrokerFillCount, candidate.ActivityJournalCount,
                candidate.EnabledConsumerLag, candidate.ObservedAtUtc,
                TradingControlIdentity.Drain(candidate))

        member this.Transition(transitionId: string) =
            use connection = this.Connect()
            use command = connection.CreateCommand()
            command.CommandText <- "SELECT payload_json FROM authority_transitions WHERE transition_id=$id"
            command.Parameters.AddWithValue("$id", transitionId) |> ignore
            use reader = command.ExecuteReader()
            if reader.Read() then Some (readTransition this reader) else None

        member this.CreateTransition(request: AuthorityTransitionRequest) =
            use connection = this.Connect()
            match operationReceipt this connection request.Operation with
            | Some receipt -> receipt
            | None ->
                let current = this.AuthorityV2()
                match Option.ofObj (TradingControlCompatibilityPolicy.Error(request, current)) with
                | Some error -> invalidArg "request" error
                | None ->
                    if not (String.IsNullOrWhiteSpace current.ActiveTransitionId) then
                        invalidOp "transition-already-active"
                    let accountGeneration =
                        Int64.Parse(this.StateValue(connection, "account_generation"))
                    if request.AccountGeneration <> accountGeneration then
                        invalidOp "stale-account-generation"
                    let view = AuthorityTransitionView(
                        TradingControlContractVersions.Current, request.TransitionId,
                        request.Direction, request.SourceMode, request.TargetMode,
                        AuthorityOwners.ForMode request.SourceMode,
                        AuthorityOwners.ForMode request.TargetMode,
                        request.SourceGeneration, request.SourceGeneration + 1L,
                        AuthorityTransitionPhases.Requested,
                        AuthorityCommandAcceptanceStates.Fenced, "", "",
                        request.AccountGeneration, request.StartedAtUtc, request.ExpiresAtUtc,
                        request.Operation.OperationId, AuthorityTransitionOutcomes.None, Array.empty)
                    use transaction = connection.BeginTransaction()
                    let receipt = persist this connection transaction view request.Operation
                    transaction.Commit()
                    receipt

        member this.ApplyTransitionStep(request: AuthorityTransitionStepRequest) =
            use connection = this.Connect()
            match operationReceipt this connection request.Operation with
            | Some receipt -> receipt
            | None ->
                match Option.ofObj (TradingControlCompatibilityPolicy.Error request) with
                | Some error -> invalidArg "request" error
                | None ->
                    use load = connection.CreateCommand()
                    load.CommandText <- "SELECT payload_json FROM authority_transitions WHERE transition_id=$id"
                    load.Parameters.AddWithValue("$id", request.TransitionId) |> ignore
                    use reader = load.ExecuteReader()
                    if not (reader.Read()) then invalidOp "authority-transition-not-found"
                    let current = readTransition this reader
                    reader.Close()
                    if current.Phase <> request.ExpectedPhase then invalidOp "authority-transition-phase-conflict"
                    if request.Operation.ObservedAtUtc > current.ExpiresAtUtc then invalidOp "authority-transition-expired"
                    let nextPhase, outcome =
                        match request.Step, current.Phase with
                        | step, phase when step = AuthorityTransitionOperations.Quiesce
                            && phase = AuthorityTransitionPhases.Requested ->
                            requireFence request.SourceFence
                            requireFence request.TargetFence
                            AuthorityTransitionPhases.Quiescing, current.Outcome
                        | step, phase when step = AuthorityTransitionOperations.Drain
                            && phase = AuthorityTransitionPhases.Quiescing ->
                            if isNull request.DrainInventory
                                || request.DrainInventory.InventoryHash
                                    <> TradingControlIdentity.Drain(request.DrainInventory) then
                                invalidOp "activity-journal-integrity-failed"
                            AuthorityTransitionPhases.Draining, current.Outcome
                        | step, phase when step = AuthorityTransitionOperations.Reconcile
                            && phase = AuthorityTransitionPhases.Draining ->
                            let drain = request.DrainInventory
                            let evidence = request.Reconciliation
                            if isNull drain || isNull evidence
                                || drain.UnresolvedIntentCount <> 0
                                || drain.UnresolvedBrokerEffectCount <> 0
                                || drain.UnprocessedBrokerFillCount <> 0
                                || drain.EnabledConsumerLag <> 0L then
                                invalidOp "unresolved-financial-intent"
                            if String.IsNullOrWhiteSpace evidence.SourceStateHash
                                || String.IsNullOrWhiteSpace evidence.BrokerReconciliationHash
                                || String.IsNullOrWhiteSpace evidence.TransferHash
                                || evidence.UnresolvedBrokerOrders <> 0 then
                                invalidOp "canonical-import-mismatch"
                            use imported = connection.CreateCommand()
                            imported.CommandText <- """SELECT COUNT(*) FROM canonical_financial_imports
WHERE transfer_id=$id AND reserved_generation=$generation AND transfer_hash=$hash"""
                            imported.Parameters.AddWithValue("$id", evidence.TransferId) |> ignore
                            imported.Parameters.AddWithValue("$generation", current.ReservedGeneration) |> ignore
                            imported.Parameters.AddWithValue("$hash", evidence.TransferHash) |> ignore
                            if Convert.ToInt32(imported.ExecuteScalar()) <> 1 then
                                invalidOp "canonical-import-mismatch"
                            AuthorityTransitionPhases.Reconciled, current.Outcome
                        | step, phase when step = AuthorityTransitionOperations.Commit
                            && phase = AuthorityTransitionPhases.Reconciled ->
                            AuthorityTransitionPhases.Verifying,
                            AuthorityTransitionOutcomes.TargetCommitted
                        | step, phase when step = AuthorityTransitionOperations.CompleteVerification
                            && phase = AuthorityTransitionPhases.Verifying ->
                            let source = request.SourceCapability
                            let target = request.TargetCapability
                            if isNull source || isNull target
                                || source.ReceiptHash <> TradingControlIdentity.Capability(source)
                                || target.ReceiptHash <> TradingControlIdentity.Capability(target)
                                || source.HasBrokerAdapter || source.HasBrokerSecret || source.HasBrokerEgress
                                || not target.HasFinancialWriter || not target.HasBrokerAdapter
                                || not target.HasBrokerSecret || not target.HasBrokerEgress then
                                invalidOp "dual-broker-capability"
                            AuthorityTransitionPhases.ReadyToRelease, current.Outcome
                        | step, phase when step = AuthorityTransitionOperations.Release
                            && phase = AuthorityTransitionPhases.ReadyToRelease ->
                            AuthorityTransitionPhases.Completed, current.Outcome
                        | step, phase when step = AuthorityTransitionOperations.Abort
                            && (phase = AuthorityTransitionPhases.Requested
                                || phase = AuthorityTransitionPhases.Quiescing
                                || phase = AuthorityTransitionPhases.Draining
                                || phase = AuthorityTransitionPhases.Reconciled) ->
                            AuthorityTransitionPhases.ReadyToRelease,
                            AuthorityTransitionOutcomes.SourceRetained
                        | _ -> invalidOp "illegal-authority-transition"
                    let sourceHash =
                        if isNull request.Reconciliation then current.SourceStateHash
                        else request.Reconciliation.SourceStateHash
                    let reconciliationHash =
                        if isNull request.Reconciliation then current.BrokerReconciliationHash
                        else request.Reconciliation.BrokerReconciliationHash
                    let acceptance =
                        if nextPhase = AuthorityTransitionPhases.Completed then
                            AuthorityCommandAcceptanceStates.Open
                        else AuthorityCommandAcceptanceStates.Fenced
                    let next = AuthorityTransitionView(
                        current.ContractVersion, current.TransitionId, current.Direction,
                        current.SourceMode, current.TargetMode, current.SourceOwner,
                        current.TargetOwner, current.SourceGeneration, current.ReservedGeneration,
                        nextPhase, acceptance, sourceHash, reconciliationHash,
                        current.AccountGeneration, current.StartedAtUtc, current.ExpiresAtUtc,
                        request.Operation.OperationId, outcome, current.StopReasons)
                    use transaction = connection.BeginTransaction()
                    if request.Step = AuthorityTransitionOperations.Commit
                        || request.Step = AuthorityTransitionOperations.Abort then
                        let mode =
                            if outcome = AuthorityTransitionOutcomes.SourceRetained then current.SourceMode
                            else current.TargetMode
                        use authority = connection.CreateCommand()
                        authority.Transaction <- transaction
                        authority.CommandText <- """UPDATE authority SET mode=$mode,generation=$generation,
authority_id=$id,activated_at=$at,previous_state_hash=$state,
broker_reconciliation_hash=$reconciliation,broker_reconciled_at=$at,
unresolved_broker_orders=0 WHERE singleton=1 AND generation=$source"""
                        authority.Parameters.AddWithValue("$mode", mode.ToString()) |> ignore
                        authority.Parameters.AddWithValue("$generation", current.ReservedGeneration) |> ignore
                        authority.Parameters.AddWithValue("$id", current.TransitionId) |> ignore
                        authority.Parameters.AddWithValue("$at", utc request.Operation.ObservedAtUtc) |> ignore
                        authority.Parameters.AddWithValue("$state", sourceHash) |> ignore
                        authority.Parameters.AddWithValue("$reconciliation", reconciliationHash) |> ignore
                        authority.Parameters.AddWithValue("$source", current.SourceGeneration) |> ignore
                        if authority.ExecuteNonQuery() <> 1 then invalidOp "authority-generation-race"
                    let receipt = persist this connection transaction next request.Operation
                    transaction.Commit()
                    receipt
