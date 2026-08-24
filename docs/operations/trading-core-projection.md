# Trading Core Projection checkpoint

This runbook records the paused Stage 5 checkpoint for ADR 0080. It does not authorize `Remote`
financial authority.

## Ownership delivered

- `stocktrader-trading-core` is an independent F# service, image, Pod, ServiceAccount, mTLS identity,
  NetworkPolicy, SQLite store, encryption secret, account generation, and health boundary.
- It owns durable entry/full-exit/partial-exit/scale-in/scale-out commands, final risk gates, broker
  attempts and evidence, reconciliation, positions, trades, inbox/outbox, and canonical read
  contracts.
- Strategy execution uses immutable artifacts and exact Market Data evidence. The existing C# engine
  and shared catalogs remain the policy owners; the F# host does not reimplement trading formulas.
- In `Projection`, the legacy API is still the only financial writer. The candidate cannot contact a
  broker because its Kubernetes egress permits DNS only.

## Verification record

Implementation and production batch on 2026-08-24 (`d9f4f30`):

- Root build and independent Trading Core build passed.
- Existing application and service integration suite: 1,008 passed, 0 failed.
- Desktop API contract check, 75 tests, and production build passed.
- The service integration test covered Projection import, encrypted account generation,
  Shadow-to-Remote fencing, duplicate entry, durable claim, entry fill, immutable position context,
  full exit, trade creation, and realized PnL.
- API and Trading Core images `architecture-d9f4f30` rolled out successfully; both Pods were Ready
  with zero restarts. `/api/health` and the desktop URL returned HTTP 200.
- Production mode was `TradingCoreTransport__Mode=Projection`; Optimization Worker, Market Data, and
  ML Training remained `Remote`.
- The candidate stored six snapshots and one account projection. Authority was `Projection`, with
  zero unresolved broker orders, zero financial intents, and zero broker evidence.
- API logs recorded successful mTLS Projection publication. Trading Core probes and Projection POSTs
  returned 200 without startup errors.
- Observed steady sample: Trading Core 18 millicores and 51 MiB; API 36 millicores and 134 MiB.
- Largest handwritten F# orchestration file was 197 nonblank lines. Direct service dependencies are
  SQLite plus the shared ServiceContracts, Engine, and TradingCore policy assemblies; duplicated
  trading-policy lines are zero by design.

The deployment script created an application-database backup before the API rollout. Preserve the
Trading Core database and its deployment secrets; neither is a substitute for the still-required
restore rehearsal.

## Resume checklist before Remote

All items below remain required as one meaningful acceptance batch:

- Route API and desktop portfolio, recommendation, position, trade, and risk reads to canonical
  Trading Core projections when Remote; prove legacy financial writes are disabled.
- Give manual orders the same immutable strategy/market/calendar/account evidence path as automatic
  orders. Never invent artifacts for historical or manually created positions.
- Run Shadow across market-open and market-closed cycles and compare stable decisions/rejections
  while both configuration and NetworkPolicy make candidate broker submission impossible.
- Exercise duplicate and ambiguous submission, timeout, delayed/out-of-order and partial fills,
  cancellation, API/Pod loss, broker outage, and restart reconciliation to one durable outcome.
- Prove bounded load, online backup/restore, database corruption handling, mTLS and encryption/auth
  secret rotation/rollback, and artifact recovery.
- Quiesce new intents, reconcile every open broker order/fill, record a monotonic cutover generation,
  prove exactly one order authority, then rehearse reconciled Remote-to-Local rollback.
- Only for the Remote deployment, replace DNS-only egress with the smallest broker endpoint policy;
  never broaden Projection or Shadow broker access.

If an imported open position lacks immutable execution context, Remote activation must fail with
`open-position-execution-context-missing`. Wait for it to close or perform a separately reviewed,
truth-preserving migration; do not fabricate historical evidence.

## Pause state and rollback

MSA work stops at this checkpoint. No Strategy Research/Edge or Reporting/Notifications extraction
is active. To roll back this non-authoritative candidate, keep the API in Local financial authority,
back up the candidate database, and scale down only `stocktrader-trading-core`. Because Projection
never submitted a broker command or owned canonical financial state, no financial state transfer is
required.
