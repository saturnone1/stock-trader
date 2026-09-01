namespace StockTrader.TradingCoreService

open System
open Microsoft.Data.Sqlite
open StockTrader.ServiceContracts.TradingCore

module Database =
    let connect path =
        let cs = $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared;Pooling=False;Default Timeout=10"
        let connection = new SqliteConnection(cs)
        connection.Open()
        connection

    let initialize path initialMode =
        match IO.Path.GetDirectoryName(IO.Path.GetFullPath path) with
        | null | "" -> ()
        | parent -> IO.Directory.CreateDirectory parent |> ignore
        use connection = connect path
        try
            use integrity = connection.CreateCommand()
            integrity.CommandText <- "PRAGMA quick_check;"
            use reader = integrity.ExecuteReader()
            if not (reader.Read()) || reader.GetString(0) <> "ok" then
                invalidOp "trading-core-database-integrity-check-failed"
        with
        | :? InvalidOperationException as error
            when error.Message = "trading-core-database-integrity-check-failed" -> raise error
        | error ->
            raise (InvalidOperationException(
                "trading-core-database-integrity-check-failed", error))
        use command = connection.CreateCommand()
        command.CommandText <- """
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS authority (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1), mode TEXT NOT NULL, generation INTEGER NOT NULL,
 authority_id TEXT NOT NULL, activated_at TEXT NOT NULL, previous_state_hash TEXT NOT NULL,
 broker_reconciliation_hash TEXT NOT NULL, broker_reconciled_at TEXT NULL,
 unresolved_broker_orders INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS snapshots (
 snapshot_id TEXT PRIMARY KEY, source_generation INTEGER NOT NULL, captured_at TEXT NOT NULL,
 payload_json TEXT NOT NULL, accepted_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS projections (
 kind TEXT NOT NULL, identity TEXT NOT NULL, payload_json TEXT NOT NULL,
 snapshot_id TEXT NOT NULL, PRIMARY KEY(kind,identity));
CREATE TABLE IF NOT EXISTS inbox (
 command_id TEXT PRIMARY KEY, command_kind TEXT NOT NULL, payload_hash TEXT NOT NULL,
 receipt_json TEXT NOT NULL, accepted_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS outbox (
 event_id TEXT PRIMARY KEY, aggregate_id TEXT NOT NULL, aggregate_version INTEGER NOT NULL,
 payload_json TEXT NOT NULL, occurred_at TEXT NOT NULL, delivered_at TEXT NULL,
 UNIQUE(aggregate_id,aggregate_version));
CREATE TABLE IF NOT EXISTS financial_intents (
 command_id TEXT PRIMARY KEY, command_kind TEXT NOT NULL, payload_hash TEXT NOT NULL,
 payload_json TEXT NOT NULL, status TEXT NOT NULL, broker_order_id TEXT NULL,
 accepted_at TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_recommendations (
 identity TEXT PRIMARY KEY, source_signal_id TEXT NOT NULL UNIQUE, payload_json TEXT NOT NULL,
 status TEXT NOT NULL, broker_order_id TEXT NULL, version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_positions (
 identity TEXT PRIMARY KEY, source_signal_id TEXT NULL UNIQUE, payload_json TEXT NOT NULL,
 execution_context_json TEXT NOT NULL, version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_trades (
 identity TEXT PRIMARY KEY, payload_json TEXT NOT NULL, version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_risk (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1), payload_json TEXT NOT NULL,
 version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_transfer_accounts (
 identity TEXT PRIMARY KEY, payload_json TEXT NOT NULL, version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_execution_identities (
 command_id TEXT PRIMARY KEY, payload_json TEXT NOT NULL, version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_transfer_broker_evidence (
 identity TEXT PRIMARY KEY, payload_json TEXT NOT NULL, version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS canonical_activity_continuity (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1), payload_json TEXT NOT NULL,
 version INTEGER NOT NULL);
CREATE TABLE IF NOT EXISTS broker_accounts (
 account_id TEXT PRIMARY KEY, payload_json TEXT NOT NULL, observed_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS broker_evidence (
 order_id TEXT PRIMARY KEY, client_order_id TEXT NOT NULL UNIQUE, command_id TEXT NOT NULL,
 payload_json TEXT NOT NULL, observed_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS shadow_entry_decisions (
 decision_id TEXT PRIMARY KEY, payload_hash TEXT NOT NULL, observation_json TEXT NOT NULL,
 receipt_json TEXT NOT NULL, is_match INTEGER NOT NULL, compared_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS shadow_execution_contexts (
 source_signal_id TEXT PRIMARY KEY, artifact_id TEXT NOT NULL, context_json TEXT NOT NULL,
 decision_id TEXT NOT NULL UNIQUE, recorded_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS shadow_position_decisions (
 decision_id TEXT PRIMARY KEY, payload_hash TEXT NOT NULL, observation_json TEXT NOT NULL,
 receipt_json TEXT NOT NULL, is_match INTEGER NOT NULL, compared_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS account_configuration (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1), generation INTEGER NOT NULL,
 configuration_hash TEXT NOT NULL, ciphertext BLOB NOT NULL, nonce BLOB NOT NULL,
 tag BLOB NOT NULL, encryption_key_generation TEXT NOT NULL DEFAULT 'legacy',
 accepted_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS encryption_key_rotation_audit (
 rotation_id TEXT PRIMARY KEY, old_generation TEXT NOT NULL, new_generation TEXT NOT NULL,
 configuration_hash TEXT NOT NULL, rotated_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS authority_transitions (
 transition_id TEXT PRIMARY KEY, phase TEXT NOT NULL, outcome TEXT NOT NULL,
 source_generation INTEGER NOT NULL, reserved_generation INTEGER NOT NULL UNIQUE,
 payload_json TEXT NOT NULL, updated_at TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ux_authority_transition_active
 ON authority_transitions((1)) WHERE phase <> 'Completed';
CREATE TABLE IF NOT EXISTS authority_transition_operations (
 operation_id TEXT PRIMARY KEY, transition_id TEXT NOT NULL, payload_hash TEXT NOT NULL,
 receipt_json TEXT NOT NULL, recorded_at TEXT NOT NULL,
 FOREIGN KEY(transition_id) REFERENCES authority_transitions(transition_id));
CREATE TABLE IF NOT EXISTS canonical_financial_imports (
 transfer_id TEXT NOT NULL, reserved_generation INTEGER NOT NULL,
 transfer_hash TEXT NOT NULL, receipt_json TEXT NOT NULL, imported_at TEXT NOT NULL,
 PRIMARY KEY(transfer_id,reserved_generation));
CREATE UNIQUE INDEX IF NOT EXISTS ux_canonical_financial_import_generation
 ON canonical_financial_imports(reserved_generation);
CREATE TABLE IF NOT EXISTS canonical_financial_exports (
 transfer_id TEXT NOT NULL, reserved_generation INTEGER NOT NULL,
 transfer_hash TEXT NOT NULL, payload_json TEXT NOT NULL, exported_at TEXT NOT NULL,
 PRIMARY KEY(transfer_id,reserved_generation));
INSERT OR IGNORE INTO state(key,value) VALUES('account_generation','0');
INSERT OR IGNORE INTO state(key,value) VALUES('last_snapshot_id','');
INSERT OR IGNORE INTO state(key,value) VALUES('last_broker_reconciliation_at','');
"""
        command.ExecuteNonQuery() |> ignore
        use columns = connection.CreateCommand()
        columns.CommandText <- "SELECT COUNT(*) FROM pragma_table_info('account_configuration') WHERE name='encryption_key_generation'"
        if Convert.ToInt32(columns.ExecuteScalar()) = 0 then
            use migrate = connection.CreateCommand()
            migrate.CommandText <- "ALTER TABLE account_configuration ADD COLUMN encryption_key_generation TEXT NOT NULL DEFAULT 'legacy'"
            migrate.ExecuteNonQuery() |> ignore
        use seed = connection.CreateCommand()
        seed.CommandText <- """INSERT OR IGNORE INTO authority
(singleton,mode,generation,authority_id,activated_at,previous_state_hash,
 broker_reconciliation_hash,broker_reconciled_at,unresolved_broker_orders)
VALUES(1,$mode,1,$id,$at,'','',NULL,0)"""
        seed.Parameters.AddWithValue("$mode", initialMode.ToString()) |> ignore
        seed.Parameters.AddWithValue("$id", TradingCoreContractVersions.Service) |> ignore
        seed.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O")) |> ignore
        seed.ExecuteNonQuery() |> ignore
