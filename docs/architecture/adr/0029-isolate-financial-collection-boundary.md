# ADR 0029: Isolate financial collection state and SEC interpretation

## Status

Accepted

## Context

The scheduled financial worker and SEC synchronization service directly created EF contexts,
mutated import-run entities, selected fallback tickers, read system time, and interpreted SEC XBRL
facts in the same orchestration code. The SEC service was over 500 lines and its annual-report
selection, amended-filing precedence, market-cap enrichment, and ratio calculations had no focused
regression tests. This made provider changes capable of silently altering research inputs.

## Decision

- `IFinancialCollectionStore` is the application port for import-run lifecycle and ticker
  projections. `FinancialCollectionStore` is its only EF Core adapter.
- File and SEC coordinators use injected `TimeProvider`; persistent timestamps and the SEC ticker-map
  cache no longer read the system clock directly.
- `SecFinancialSyncPolicy` owns provider identity, symbol precedence and normalization, limits,
  automatic-run interval semantics, compatible run labels, fingerprints, and request/cache timing.
- `SecFinancialDocumentParser` deterministically extracts supported annual facts, chooses the latest
  amended filing for a reporting date, and ignores quarterly observations for annual metrics.
- `SecFinancialSnapshotFactory` owns market-cap enrichment and PE, PB, ROE, and operating-margin
  formulas. The SEC coordinator now only selects symbols, calls transports, and sequences import.
- Automatic interval checks continue to accept any completed SEC run, while displayed “latest
  success” continues to require at least one imported item. File fingerprint idempotency and failed
  run restart behavior remain unchanged.

## Consequences

Provider JSON interpretation and financial arithmetic can be tested without HTTP or a database.
Collection persistence can evolve independently of workers, and API/application orchestration no
longer imports EF entities. SEC transport remains an infrastructure service because it coordinates
remote calls and Yahoo price enrichment; moving provider clients behind narrower ports is a later
step. No schema migration or response-contract change is introduced by this decision.
