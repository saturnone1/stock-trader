namespace StockTrader.TradingCoreService

open System
open System.Text.Json
open StockTrader.ServiceContracts
open StockTrader.ServiceContracts.TradingCore
open StockTrader.TradingCore.Broker
open StockTrader.TradingCore.Execution

[<AutoOpen>]
module TradingCoreTransferStore =
    let private readTransferList<'T when 'T : not struct and 'T : not null>
        (store: TradingCoreStore) (connection: Microsoft.Data.Sqlite.SqliteConnection)
        table orderBy =
        use command = connection.CreateCommand()
        command.CommandText <- $"SELECT payload_json FROM {table} ORDER BY {orderBy}"
        use reader = command.ExecuteReader()
        let values = ResizeArray<'T>()
        while reader.Read() do
            match Option.ofObj (JsonSerializer.Deserialize<'T>(reader.GetString 0, store.Json)) with
            | Some value -> values.Add value
            | None -> invalidOp $"empty-{table}-payload"
        values.ToArray()

    let private executionIdentities (store: TradingCoreStore)
        (connection: Microsoft.Data.Sqlite.SqliteConnection) =
        use command = connection.CreateCommand()
        command.CommandText <- """SELECT f.command_id,f.payload_json,f.payload_hash,f.status,
COALESCE(e.client_order_id,''),COALESCE(e.order_id,f.broker_order_id,''),
COALESCE(e.observed_at,f.updated_at)
FROM financial_intents f LEFT JOIN broker_evidence e ON e.command_id=f.command_id
ORDER BY f.command_id"""
        use reader = command.ExecuteReader()
        [| while reader.Read() do
            use payload = JsonDocument.Parse(reader.GetString 1)
            let root = payload.RootElement
            let sourceIdentity =
                match root.TryGetProperty("sourceSignalId") with
                | true, value -> value.GetString()
                | _ -> root.GetProperty("positionId").GetString()
            let commandId = reader.GetString 0
            let clientId =
                if reader.IsDBNull 4 || String.IsNullOrWhiteSpace(reader.GetString 4) then
                    FinancialExecutionIdentityPolicy.ClientOrderId commandId
                else reader.GetString 4
            yield FinancialExecutionIdentity(sourceIdentity, commandId, clientId,
                reader.GetString 5, reader.GetString 3, reader.GetString 2,
                DateTime.Parse(reader.GetString 6, null,
                    Globalization.DateTimeStyles.RoundtripKind)) |]

    let private currentBrokerEvidence (store: TradingCoreStore)
        (connection: Microsoft.Data.Sqlite.SqliteConnection)
        (portfolio: TradingCorePortfolioView) =
        let brokerPositions = Collections.Generic.Dictionary<string,int>(StringComparer.Ordinal)
        use positionsCommand = connection.CreateCommand()
        positionsCommand.CommandText <- "SELECT account_id,symbol,payload_json FROM broker_positions ORDER BY account_id,symbol"
        use positionsReader = positionsCommand.ExecuteReader()
        while positionsReader.Read() do
            let value = JsonSerializer.Deserialize<BrokerPositionEvidence>(positionsReader.GetString 2, store.Json)
            if isNull value then invalidOp "empty-broker-position-evidence"
            brokerPositions[$"{positionsReader.GetString 0}|{positionsReader.GetString 1}"] <- value.Quantity
        positionsReader.Close()
        use command = connection.CreateCommand()
        command.CommandText <- """SELECT f.command_kind,f.payload_json,e.payload_json,e.observed_at
FROM broker_evidence e JOIN financial_intents f ON f.command_id=e.command_id
ORDER BY e.client_order_id,e.order_id"""
        use reader = command.ExecuteReader()
        [| while reader.Read() do
            use intent = JsonDocument.Parse(reader.GetString 1)
            let root = intent.RootElement
            let evidence = JsonSerializer.Deserialize<BrokerOrderEvidence>(reader.GetString 2, store.Json)
            if isNull evidence then invalidOp "empty-broker-order-evidence"
            let accountId, symbol =
                if reader.GetString 0 = TradingCommandKinds.AcceptEntry then
                    root.GetProperty("accountId").GetString(), root.GetProperty("symbol").GetString()
                else
                    let positionId = root.GetProperty("positionId").GetString()
                    match portfolio.Positions |> Seq.tryFind (fun value -> value.PositionId = positionId) with
                    | Some position -> position.AccountId, position.Symbol
                    | None -> invalidOp "position-command-transfer-identity-missing"
            let normalizedSymbol = symbol.ToUpperInvariant()
            let canonicalQuantity =
                portfolio.Positions
                |> Seq.filter (fun value ->
                    (value.AccountId = accountId)
                    && value.Symbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase)
                    && (not value.ClosedAtUtc.HasValue))
                |> Seq.sumBy _.Quantity
            let mutable brokerQuantity = 0
            brokerPositions.TryGetValue($"{accountId}|{normalizedSymbol}", &brokerQuantity) |> ignore
            let observed = DateTime.Parse(reader.GetString 3, null,
                               Globalization.DateTimeStyles.RoundtripKind)
            let candidate = FinancialBrokerEvidence(accountId, normalizedSymbol,
                canonicalQuantity, brokerQuantity, evidence.ClientOrderId,
                evidence.OrderId, evidence.Side, evidence.Quantity,
                evidence.FilledQuantity, evidence.Status, observed, "")
            yield FinancialBrokerEvidence(accountId, normalizedSymbol, canonicalQuantity,
                brokerQuantity, evidence.ClientOrderId, evidence.OrderId, evidence.Side,
                evidence.Quantity, evidence.FilledQuantity, evidence.Status, observed,
                CanonicalFinancialTransferIdentity.BrokerEvidence candidate) |]

    type TradingCoreStore with
        member this.ExportFinancialTransfer(request: CanonicalFinancialExportRequest) =
            match Option.ofObj (CanonicalFinancialTransferPolicy.Error request) with
            | Some error -> invalidArg "request" error
            | None -> ()
            if request.Direction <> AuthorityTransitionDirections.Rollback
                || request.SourceMode <> TradingAuthorityMode.Remote
                then
                invalidArg "request" "invalid-financial-export-request"
            let transition =
                match this.Transition request.TransitionId with
                | Some value -> value
                | None -> invalidOp "authority-transition-not-found"
            if transition.Phase <> AuthorityTransitionPhases.Draining
                || transition.SourceGeneration <> request.SourceGeneration
                || transition.ReservedGeneration <> request.ReservedTargetGeneration then
                invalidOp "authority-transition-phase-conflict"
            let configuration =
                match this.AccountConfiguration() with
                | Some value -> value
                | None -> invalidOp "account-configuration-unavailable"
            let portfolio = this.Portfolio()
            let captured = request.Operation.ObservedAtUtc
            let candidate = TradingStateSnapshot(
                TradingCoreContractVersions.Current, "", request.SourceGeneration,
                captured, configuration.Accounts |> Seq.map (fun account ->
                    TradingAccountProjection(account.AccountId, account.BrokerCode,
                        account.Environment, account.IsEnabled, account.IsActive,
                        configuration.Generation)) |> Seq.toArray,
                portfolio.Recommendations, portfolio.Positions, portfolio.Trades,
                portfolio.Risk)
            let snapshot = TradingStateSnapshot(
                candidate.ContractVersion, TradingCoreIdentity.Snapshot(candidate),
                candidate.SourceGeneration, candidate.CapturedAtUtc, candidate.Accounts,
                candidate.Recommendations, candidate.Positions, candidate.Trades,
                candidate.Risk)
            use connection = this.Connect()
            let importedIdentities = readTransferList<FinancialExecutionIdentity>
                                         this connection "canonical_execution_identities" "command_id"
            let identities =
                Seq.append importedIdentities (executionIdentities this connection)
                |> Seq.groupBy _.CommandId
                |> Seq.map (fun (_, values) -> values |> Seq.last)
                |> Seq.sortBy _.CommandId
                |> Seq.toArray
            let importedBrokerEvidence = readTransferList<FinancialBrokerEvidence>
                                             this connection "canonical_transfer_broker_evidence" "identity"
            let brokerEvidence =
                Seq.append importedBrokerEvidence (currentBrokerEvidence this connection portfolio)
                |> Seq.groupBy (fun value -> $"{value.AccountId}|{value.ClientOrderId}|{value.BrokerOrderId}")
                |> Seq.map (fun (_, values) -> values |> Seq.last)
                |> Seq.sortBy (fun value -> $"{value.AccountId}|{value.ClientOrderId}|{value.BrokerOrderId}")
                |> Seq.toArray
            use journal = connection.CreateCommand()
            journal.CommandText <- "SELECT COUNT(*) FROM outbox"
            let journalCount = Convert.ToInt64(journal.ExecuteScalar())
            use versionsCommand = connection.CreateCommand()
            versionsCommand.CommandText <- "SELECT aggregate_id,MAX(aggregate_version) FROM outbox GROUP BY aggregate_id ORDER BY aggregate_id"
            use versionReader = versionsCommand.ExecuteReader()
            let versions = Collections.Generic.SortedDictionary<string,int64>(StringComparer.Ordinal)
            while versionReader.Read() do versions.Add(versionReader.GetString 0, versionReader.GetInt64 1)
            let activity = CanonicalFinancialTransferMapper.Activity(
                versions, journalCount, Array.Empty<FinancialConsumerCursor>())
            let transfer = CanonicalFinancialTransferMapper.Create(
                request.TransferId, request.TransitionId, request.Direction,
                request.SourceMode, request.ReservedTargetGeneration,
                request.Compatibility, configuration, snapshot, identities,
                brokerEvidence, activity, request.EquityBasis)
            use persist = connection.CreateCommand()
            persist.CommandText <- """INSERT INTO canonical_financial_exports
(transfer_id,reserved_generation,transfer_hash,payload_json,exported_at)
VALUES($id,$generation,$hash,$payload,$at)
ON CONFLICT(transfer_id,reserved_generation) DO NOTHING"""
            persist.Parameters.AddWithValue("$id", transfer.TransferId) |> ignore
            persist.Parameters.AddWithValue("$generation", transfer.ReservedTargetGeneration) |> ignore
            persist.Parameters.AddWithValue("$hash", transfer.TransferHash) |> ignore
            persist.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(transfer, this.Json)) |> ignore
            persist.Parameters.AddWithValue("$at", transfer.CapturedAtUtc.ToString("O")) |> ignore
            if persist.ExecuteNonQuery() = 0 then
                use existing = connection.CreateCommand()
                existing.CommandText <- """SELECT transfer_hash FROM canonical_financial_exports
WHERE transfer_id=$id AND reserved_generation=$generation"""
                existing.Parameters.AddWithValue("$id", transfer.TransferId) |> ignore
                existing.Parameters.AddWithValue("$generation", transfer.ReservedTargetGeneration) |> ignore
                if Convert.ToString(existing.ExecuteScalar()) <> transfer.TransferHash then
                    invalidOp "transfer-identity-conflict"
            transfer

        member this.RecordExternalFinancialImport(receipt: CanonicalFinancialImportReceipt) =
            if isNull receipt
                || receipt.ContractVersion <> CanonicalFinancialTransferVersions.Current
                || not (Guid.TryParse(receipt.TransferId) |> fst)
                || receipt.ReservedGeneration < 2L
                || String.IsNullOrWhiteSpace receipt.TransferHash
                || String.IsNullOrWhiteSpace receipt.ImportStateHash then
                invalidArg "receipt" "invalid-financial-import-receipt"
            use connection = this.Connect()
            use exported = connection.CreateCommand()
            exported.CommandText <- """SELECT transfer_hash FROM canonical_financial_exports
WHERE transfer_id=$id AND reserved_generation=$generation"""
            exported.Parameters.AddWithValue("$id", receipt.TransferId) |> ignore
            exported.Parameters.AddWithValue("$generation", receipt.ReservedGeneration) |> ignore
            match exported.ExecuteScalar() with
            | null -> invalidOp "canonical-financial-export-not-found"
            | value when Convert.ToString(value) <> receipt.TransferHash ->
                invalidOp "canonical-import-mismatch"
            | _ -> ()
            use command = connection.CreateCommand()
            command.CommandText <- """INSERT INTO canonical_financial_imports
(transfer_id,reserved_generation,transfer_hash,receipt_json,imported_at)
VALUES($id,$generation,$hash,$receipt,$at)
ON CONFLICT(transfer_id,reserved_generation) DO NOTHING"""
            command.Parameters.AddWithValue("$id", receipt.TransferId) |> ignore
            command.Parameters.AddWithValue("$generation", receipt.ReservedGeneration) |> ignore
            command.Parameters.AddWithValue("$hash", receipt.TransferHash) |> ignore
            command.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
            command.Parameters.AddWithValue("$at", receipt.ImportedAtUtc.ToString("O")) |> ignore
            let inserted = command.ExecuteNonQuery() = 1
            CanonicalFinancialImportReceipt(
                receipt.ContractVersion, receipt.TransferId, receipt.TransferHash,
                receipt.ReservedGeneration, receipt.ImportStateHash, not inserted,
                receipt.ImportedAtUtc)

        member this.ImportFinancialTransfer(transfer: CanonicalFinancialTransferV2) =
            match Option.ofObj (CanonicalFinancialTransferPolicy.Error transfer) with
            | Some error -> invalidArg "transfer" error
            | None ->
                let transition =
                    match this.Transition transfer.TransitionId with
                    | Some value -> value
                    | None -> invalidOp "authority-transition-not-found"
                if transition.Phase <> AuthorityTransitionPhases.Draining
                    || transition.ReservedGeneration <> transfer.ReservedTargetGeneration
                    || transition.SourceGeneration <> transfer.SourceGeneration
                    || transition.Direction <> transfer.Direction
                    || transition.SourceMode <> transfer.SourceMode then
                    invalidOp "authority-transition-phase-conflict"
                if transfer.Accounts |> Seq.exists (fun account ->
                    account.ConfigurationGeneration <> transition.AccountGeneration) then
                    invalidOp "stale-account-generation"
                let snapshot = CanonicalFinancialTransferMapper.Snapshot transfer
                match Option.ofObj (TradingCoreCompatibilityPolicy.Error snapshot) with
                | Some error -> invalidArg "transfer" error
                | None -> ()
                if snapshot.Positions |> Seq.exists (fun position ->
                    not position.ClosedAtUtc.HasValue
                    && (isNull position.ExecutionContext
                        || isNull position.ExecutionContext.ExecutionArtifact.PositionManagement
                        || not position.LastEvaluatedBarUtc.HasValue)) then
                    invalidOp "open-position-autonomous-protection-incompatible"
                use connection = this.Connect()
                use existing = connection.CreateCommand()
                existing.CommandText <- """SELECT transfer_hash,receipt_json FROM canonical_financial_imports
WHERE transfer_id=$id AND reserved_generation=$generation"""
                existing.Parameters.AddWithValue("$id", transfer.TransferId) |> ignore
                existing.Parameters.AddWithValue("$generation", transfer.ReservedTargetGeneration) |> ignore
                use reader = existing.ExecuteReader()
                if reader.Read() then
                    let storedHash, receiptJson = reader.GetString 0, reader.GetString 1
                    reader.Close()
                    if storedHash <> transfer.TransferHash then invalidOp "transfer-identity-conflict"
                    let receipt = JsonSerializer.Deserialize<CanonicalFinancialImportReceipt>(receiptJson, this.Json)
                    if isNull receipt then invalidOp "empty-financial-import-receipt"
                    CanonicalFinancialImportReceipt(
                        receipt.ContractVersion, receipt.TransferId, receipt.TransferHash,
                        receipt.ReservedGeneration, receipt.ImportStateHash, true,
                        receipt.ImportedAtUtc)
                else
                    reader.Close()
                    use transaction = connection.BeginTransaction()
                    use clear = connection.CreateCommand()
                    clear.Transaction <- transaction
                    clear.CommandText <- """
DELETE FROM canonical_recommendations;
DELETE FROM canonical_positions;
DELETE FROM canonical_trades;
DELETE FROM canonical_risk;
DELETE FROM canonical_transfer_accounts;
DELETE FROM canonical_execution_identities;
DELETE FROM canonical_transfer_broker_evidence;
DELETE FROM canonical_activity_continuity;
"""
                    clear.ExecuteNonQuery() |> ignore
                    for account in transfer.Accounts do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_transfer_accounts VALUES($id,$payload,1)"
                        command.Parameters.AddWithValue("$id", account.AccountId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(account, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    for recommendation in snapshot.Recommendations do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_recommendations VALUES($id,$signal,$payload,$status,$order,1)"
                        command.Parameters.AddWithValue("$id", recommendation.RecommendationId) |> ignore
                        command.Parameters.AddWithValue("$signal", recommendation.SourceSignalId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(recommendation, this.Json)) |> ignore
                        command.Parameters.AddWithValue("$status", "Imported") |> ignore
                        command.Parameters.AddWithValue("$order",
                            if String.IsNullOrWhiteSpace recommendation.EntryOrderId then box DBNull.Value
                            else box recommendation.EntryOrderId) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    for position in snapshot.Positions do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_positions VALUES($id,$signal,$payload,$context,1)"
                        command.Parameters.AddWithValue("$id", position.PositionId) |> ignore
                        command.Parameters.AddWithValue("$signal",
                            if String.IsNullOrWhiteSpace position.SourceSignalId then box DBNull.Value
                            else box position.SourceSignalId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(position, this.Json)) |> ignore
                        command.Parameters.AddWithValue("$context",
                            if isNull position.ExecutionContext then "{}"
                            else JsonSerializer.Serialize(position.ExecutionContext, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    for trade in snapshot.Trades do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_trades VALUES($id,$payload,1)"
                        command.Parameters.AddWithValue("$id", trade.TradeId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(trade, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    use risk = connection.CreateCommand()
                    risk.Transaction <- transaction
                    risk.CommandText <- "INSERT INTO canonical_risk VALUES(1,$payload,1)"
                    risk.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(snapshot.Risk, this.Json)) |> ignore
                    risk.ExecuteNonQuery() |> ignore
                    for identity in transfer.ExecutionIdentities do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_execution_identities VALUES($id,$payload,1)"
                        command.Parameters.AddWithValue("$id", identity.CommandId) |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(identity, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    for evidence in transfer.BrokerEvidence do
                        use command = connection.CreateCommand()
                        command.Transaction <- transaction
                        command.CommandText <- "INSERT INTO canonical_transfer_broker_evidence VALUES($id,$payload,1)"
                        command.Parameters.AddWithValue("$id", $"{evidence.AccountId}|{evidence.ClientOrderId}|{evidence.BrokerOrderId}") |> ignore
                        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(evidence, this.Json)) |> ignore
                        command.ExecuteNonQuery() |> ignore
                    use activity = connection.CreateCommand()
                    activity.Transaction <- transaction
                    activity.CommandText <- "INSERT INTO canonical_activity_continuity VALUES(1,$payload,1)"
                    activity.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(transfer.Activity, this.Json)) |> ignore
                    activity.ExecuteNonQuery() |> ignore
                    let receipt = CanonicalFinancialImportReceipt(
                        CanonicalFinancialTransferVersions.Current, transfer.TransferId,
                        transfer.TransferHash, transfer.ReservedTargetGeneration,
                        snapshot.SnapshotId, false, transfer.CapturedAtUtc)
                    use record = connection.CreateCommand()
                    record.Transaction <- transaction
                    record.CommandText <- """INSERT INTO canonical_financial_imports
(transfer_id,reserved_generation,transfer_hash,receipt_json,imported_at)
VALUES($id,$generation,$hash,$receipt,$at)"""
                    record.Parameters.AddWithValue("$id", transfer.TransferId) |> ignore
                    record.Parameters.AddWithValue("$generation", transfer.ReservedTargetGeneration) |> ignore
                    record.Parameters.AddWithValue("$hash", transfer.TransferHash) |> ignore
                    record.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt, this.Json)) |> ignore
                    record.Parameters.AddWithValue("$at", transfer.CapturedAtUtc.ToString("O")) |> ignore
                    record.ExecuteNonQuery() |> ignore
                    transaction.Commit()
                    receipt
