# 0078 — Extract Market Data as the exclusive normalized-bar owner

- Status: Accepted
- Date: 2026-08-23
- Baseline: b6f4a24

## Context

Optimization Worker is the first completed MSA service. The next boundary is market data, where the
current application still mixes three responsibilities: provider access (Alpaca, Yahoo, and LS),
normalized OHLCV persistence in the application SQLite database, and trading/research consumption.
The `OhlcvBars` identity also omits provider, adjustment mode, evidence version, and correction
revision. A second Pod that merely reads or writes that table would create a shared-database service,
not an independently owned boundary.

Market-data corrections are economically meaningful. Preview, backtest, optimization, scanning, and
live position decisions must either consume the same evidence identity or reject stale/incomplete
data. A cutover therefore needs stronger evidence than matching a small set of prices.

## Decision

Create an F# Market Data service with its own OCI image, Kubernetes Deployment, ServiceAccount,
network policy, mTLS identity, shared-secret application credential, health/readiness endpoints,
metrics, and service-owned SQLite volume. It is the exclusive writer of normalized bars and
correction revisions. It has no application database, broker order, trading account, strategy, or
financial-write access.

The versioned contract carries provider, symbol, timeframe, adjustment mode, market/calendar
version, requested range, completeness, content hash, revision, and normalized bars. Stored identity
is `(provider, symbol, timeframe, adjustment mode, timestamp)`. Changing an existing bar creates a
monotonic correction record; consumers cache only by the returned evidence identity.

Provider REST and streaming credentials enter only the Market Data Pod. The application selects a
provider as user policy, but calls it through the remote contract. Historical/latest/current-price
requests, daily/intraday ingestion, and realtime bar persistence no longer invoke a provider SDK or
write `OhlcvBars` in `Remote` mode.

The application keeps compatibility adapters behind `MarketDataTransport.Mode`:

- `Local`: legacy provider adapters and application SQLite are authoritative;
- `Shadow`: legacy reads/writes remain authoritative while a backfill and exact evidence-aware
  comparison populate and verify the service;
- `Remote`: provider access and normalized-bar reads/writes terminate at Market Data. The legacy
  table is read-only and receives no dual writes.

An authenticated, idempotent import endpoint is the only migration writer. Backfill is resumable by
stable batch identity. Cutover requires range/count/content-hash parity for every stored provider,
symbol, timeframe, and adjustment identity. Corrections after cutover are replayed by revision, not
by blind dual writes.

## Failure and rollback policy

Trading Core and live consumers fail closed when the service is unavailable, evidence is incomplete,
or a required correction revision is newer than the acknowledged evidence. Research surfaces the
exact degradation. It may not silently fetch from a different provider.

Rollback changes both application and Market Data deployment configuration to `Local`; it does not
copy service corrections backward automatically. Before rollback, the compatibility verifier must
prove that the legacy read model contains the required evidence or operations must restore the
pre-cutover database backup. The Market Data database has an independent backup/restore path and is
never mounted by the application Pod.

## Deployment and persistence

The service uses one replica while SQLite is the canonical store. Horizontal HTTP replicas are not
allowed to mount the same host database. Provider request concurrency is bounded independently of
application request concurrency. A future multi-replica store requires a new ADR and a database with
a single-writer/replication model.

The supported `scripts/deploy-k3s.sh` path builds/imports the image, backs up both databases before a
cutover, injects generation-scoped TLS Secrets, rolls the Market Data Pod, then rolls consumers.
Backup, restore, certificate rotation, provider outage, correction replay, stale evidence rejection,
and `Remote`/`Local` rollback are part of one final service-level verification batch.

## Agent working-set budget

The F# host is split by responsibility. No orchestration file should exceed 200 nonblank lines; the
minimal API composition file should stay below 150. Shared contracts contain no provider or trading
policy, and duplicated timeframe/provider/catalog policy has a target of zero. Provider protocol
adapters may exceed the shell budget only when their pagination/authentication state machine would
be less reviewable if compressed.

## Acceptance evidence

The production service-level batch passed on 2026-08-24 KST at implementation head `6587b65`.
The full command-level record and recovery points are in
[Market Data cutover operations](../../operations/market-data-cutover.md). The batch proved the
independent F# image/Pod/database boundary, exact six-series Shadow parity, authenticated provider
REST and WebSocket paths, Remote preview/backtest/optimization consumption, absence of legacy dual
writes, service and provider outage behavior, Pod persistence, parallel load, backup/restore, TLS
rotation and preserved-generation rollback, and verified Remote-to-Local projection followed by
Remote recutover.

The drill found and fixed four release-blocking integration defects: API liveness depended on the
Market Data readiness check, protected backups could not pass the restore script's privilege
boundary, SQLite decimal/boundary representation made rollback verification incorrect, and the
Alpaca stream success token was parsed as `authorized` instead of `authenticated`. The final batch
reran the affected lifecycle gates after each correction.

The following requirements remain the regression gate for later releases:

This ADR becomes Accepted only when one batch proves:

- independent build, image, Pod, ServiceAccount, volume, mTLS, secret, probes, metrics, and logs;
- legacy backfill and exact parity across every populated provider/symbol/timeframe range;
- application `Remote` mode with no `OhlcvBars` write and no in-process provider call;
- historical, latest, intraday, current-price, daily sync, and realtime ingestion paths;
- provider outage, timeout, pagination, rate limiting, and explicit no-fallback behavior;
- correction revision, cache invalidation, stale/incomplete evidence rejection, and replay;
- Pod/API restart, idempotent import, duplicate request, load, backup/restore, and resource evidence;
- certificate rotation and preserved-generation rollback;
- `Remote` to `Local` rollback with the required legacy evidence restored;
- preview/backtest/optimization results remain tied to explicit, matching market-data evidence.

These conditions passed for the accepted release. ML Training is the next extraction boundary; it
must not change Market Data ownership or reopen a legacy normalized-bar writer.
