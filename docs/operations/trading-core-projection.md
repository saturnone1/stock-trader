# Trading Core Projection checkpoint

This runbook records the active Stage 5 Projection checkpoint for ADR 0080. It does not authorize `Remote`
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

## Acceptance checklist before Remote

Completed code-side prerequisites:

- Route API and desktop portfolio, recommendation, position, trade, and risk reads to canonical
  Trading Core projections when Remote; prove legacy financial writes are disabled.
- Give manual orders the same immutable strategy/market/calendar/account evidence path as automatic
  orders. Never invent artifacts for historical or manually created positions.
- Reject expired commands before broker submission, retain ambiguous post-submission commands, and
  converge restart, partial-fill, terminal-partial, and contradictory-fill evidence safely.

Remaining production acceptance gates:

- Run Shadow across market-open and market-closed cycles and compare stable decisions/rejections
  while both configuration and NetworkPolicy make candidate broker submission impossible.
- Exercise duplicate and ambiguous submission, timeout, delayed/out-of-order fills, cancellation,
  API/Pod loss, and broker outage against controlled broker evidence in K3s.
- Prove bounded load, online backup/restore, database corruption handling, mTLS and encryption/auth
  secret rotation/rollback, and artifact recovery.
- Quiesce new intents, reconcile every open broker order/fill, record a monotonic cutover generation,
  prove exactly one order authority, then rehearse reconciled Remote-to-Local rollback.
- Only for the Remote deployment, replace DNS-only egress with the smallest broker endpoint policy;
  never broaden Projection or Shadow broker access.

### 2026-08-27 resume and failure-convergence audit

The first resumed service unit completed the Remote read routers, read-only compatibility stores,
manual completed-bar evidence path, AlertOnly recommendation path, immutable position-context
resolver, exact-evidence position evaluation, canonical policy-state update, account/risk views,
latest-command reconciliation queries, and broker/canonical divergence fencing. The meaningful
local batch passed 1,010 backend tests, 75 desktop tests, API contract checking, independent API and
Trading Core builds, and the desktop production build.

API `e96f5a2` and Trading Core `c10c404` were then deployed in Projection. The authoritative Trading
Core image is `architecture-c10c404-r1`; an earlier `architecture-c10c404` tag was built before the
server fast-forward and was immediately superseded, so it must not be reused. The supported deploy
script created backups
`trading-core-pre-e96f5a2-20260827T094919Z.db`,
`stocktrader-pre-e96f5a2-20260827T095149Z.db`, and
`trading-core-pre-c10c404-r1-20260827T100141Z.db`.

The second service unit made pre-broker expiry fail closed and retained post-submission commands for
reconciliation. Partial evidence survives a process reopen. A terminal cancelled/rejected/expired
order with a proven non-zero fill commits only that quantity; a contradictory `Filled` quantity
remains `ReconciliationRequired`. The characterization test covers partial entry, process restart,
terminal partial entry, partial exit, contradictory terminal evidence, corrected terminal partial
exit, and deterministic queue expiry. The complete batch passed 1,012 backend tests, 75 desktop
tests, API contract checking, both service/application builds, and the desktop production build.

After rollout all StockTrader Pods were Ready with zero restarts. `/api/health` returned HTTP 200;
Trading Core reported `ready=true`, `mode=Projection`, and `lastError=null`. Its database had zero
financial intents, broker evidence, and open positions. The candidate remains unable to reach a
broker. These facts allow Shadow preparation but do not satisfy Shadow, live failure, restore,
rotation, load, cutover, or rollback acceptance.

If an imported open position lacks immutable execution context, Remote activation must fail with
`open-position-execution-context-missing`. Wait for it to close or perform a separately reviewed,
truth-preserving migration; do not fabricate historical evidence.

### 2026-08-27 Shadow-boundary deployment audit

Entry Shadow (`517c80e`), entry-context preservation (`2cd90f5`), and position Shadow (`3866ca8`)
are implemented. The position comparison includes order disposition/action/quantity and highest
price, stop, initial-risk, breakeven, and trailing policy state; otherwise a no-order cycle could
incorrectly report parity while changing future execution behavior. Comparison storage is
idempotent and has no canonical financial or broker mutation path.

The complete local batch passed 1,014 backend tests, 75 desktop tests, API contract checking, and
both production builds. API and Trading Core image `architecture-3866ca8` rolled out through
`scripts/deploy-k3s.sh` in Projection. Backups were created at
`/var/lib/stocktrader/trading-core/backups/trading-core-pre-3866ca8-20260827T102751Z.db` and
`/home/saturnone1/stock-trader-data/backups/stocktrader-pre-3866ca8-20260827T102916Z.db`.

Post-rollout evidence: every Pod was Ready with zero restarts, `/api/health` returned 200, Trading
Core reported Projection generation 1 and no error, and the database contained the entry,
execution-context, and position Shadow tables with zero rows. Financial intents, broker evidence,
and canonical positions also remained zero. No Shadow authority or broker egress was enabled.

An initial operator invocation supplied the script-owned `architecture-` prefix twice and produced
the temporary tag `architecture-architecture-3866ca8`. It was immediately superseded by the same
image content under `architecture-3866ca8`; never use the doubled tag for rollout or rollback.

### 2026-08-27 Shadow activation

Trading Core was first redeployed with `STOCKTRADER_TRADING_CORE_MODE=Shadow` while the durable
authority remained Projection generation 1. The authenticated mTLS authority endpoint then advanced
it to generation 2 with authority ID `shadow-3866ca8-g2`; the API was subsequently redeployed with
`STOCKTRADER_TRADING_CORE_MODE=Shadow`. This ordering kept Local authoritative throughout the
transition. The additional pre-activation Trading Core backup is
`/var/lib/stocktrader/trading-core/backups/trading-core-pre-3866ca8-20260827T103320Z.db`, and the
API Shadow rollout backup is
`/home/saturnone1/stock-trader-data/backups/stocktrader-pre-3866ca8-20260827T103434Z.db`.

After activation, API health returned 200 and reported Shadow generation 2, zero comparison rows,
zero intents, zero broker evidence, and no Trading Core error. Every Pod was Ready with zero
restarts. The Trading Core NetworkPolicy still permits only UDP/TCP DNS egress, so candidate broker
submission is physically unavailable. Projection publication continued successfully. The new API
Pod's first full health request was cancelled while opening the application database and returned
one 500; subsequent health requests returned 200 with no recurring error or restart.

There were no open positions or order attempts during the initial closed-market observation, so the
zero comparison count is not accepted as parity. Keep Shadow active to collect genuine closed/open
market decisions before beginning failure drills or any Remote cutover preparation.

### 2026-08-27 Shadow Pod-loss drill

During the closed-market Shadow window, operator checks first resolved the exact running Pod names.
Deleting `stocktrader-trading-core-57f9f6c79b-2gdvj` caused K3s to create
`stocktrader-trading-core-57f9f6c79b-8pvkm`, which was Ready after approximately 12 seconds. Shadow
generation 2 and the persistent database survived; financial intents and broker evidence remained
zero, API health returned 200, and Projection publication continued without a logged error.

Deleting `stocktrader-api-5c756c47bd-zzmm5` then caused K3s to create
`stocktrader-api-5c756c47bd-c579p`. The API deployment was Ready within approximately one minute,
returned health 200, reported the same Shadow generation 2, and resumed Projection publication. All
StockTrader Pods were Ready with zero restarts and the new API Pod had no ERR, FTL, or 500 log after
startup. The exercise proves non-authoritative Pod recreation and durable-state continuity only; it
does not replace controlled broker-evidence or Remote single-writer failure drills.

## Supported non-Remote restore

Use `scripts/restore-trading-core-backup.sh` only with a verified backup inside the configured
Trading Core `backups/` directory. The script refuses Remote authority and any mode/generation
mismatch, scales API and Trading Core down together, creates and verifies a pre-restore rollback
copy, restores through a checked staging database, removes only the stopped database's exact WAL/SHM
companions, and starts Trading Core before API. Example:

```bash
STOCKTRADER_TRADING_CORE_DIR=/var/lib/stocktrader/trading-core \
  scripts/restore-trading-core-backup.sh \
  /var/lib/stocktrader/trading-core/backups/<shadow-generation-2-backup>.db
```

Remote disaster recovery remains prohibited through this script because it requires separately
reconciling every in-flight broker order and preserving the single-writer cutover generation.

### 2026-08-27 Shadow restore rehearsal

An online generation 2 Shadow backup was created at
`/var/lib/stocktrader/trading-core/backups/trading-core-shadow-g2-restore-drill-20260827T104300Z.db`.
The supported restore script accepted its matching Shadow/generation metadata, stopped API and
Trading Core, created the rollback copy
`/var/lib/stocktrader/trading-core/backups/trading-core-before-restore-20260827T104502Z.db`, restored
through staging, and started Trading Core before API.

After the rehearsal all Pods were Ready with zero restarts, `/api/health` returned 200, database
integrity was `ok`, authority remained `Shadow|2|shadow-3866ca8-g2`, and financial intents and broker
evidence remained zero. Projection publication resumed and the new API logs had no ERR, FTL, or 500.
This completes the non-Remote online restore rehearsal. Isolated corruption detection, TLS/secret
rotation, and Remote reconciled disaster recovery remain separate gates.

The service subsequently added a startup `PRAGMA quick_check` before schema initialization. A local
characterization truncates a valid candidate database and verifies the named
`trading-core-database-integrity-check-failed` exception, proving that corruption cannot silently
create a fresh financial store. Production rollout evidence for this startup guard is recorded with
its image checkpoint; TLS/secret rotation and Remote recovery remain separate gates.

Trading Core image `architecture-0c7dc49` rolled out the guard in Shadow and created backup
`/var/lib/stocktrader/trading-core/backups/trading-core-pre-0c7dc49-20260827T104932Z.db`. The API
remained on contract-compatible `architecture-3866ca8`. The Trading Core Pod was Ready with zero
restarts, API health returned 200, and Shadow generation 2 remained error-free after startup.

## Current state and rollback

MSA work continues only on Trading Core Stage 5. No Strategy Research/Edge or
Reporting/Notifications extraction is active. To roll back this non-authoritative candidate, keep
the API in Local financial authority,
back up the candidate database, and scale down only `stocktrader-trading-core`. Because Projection
and Shadow never submitted a candidate broker command or owned canonical financial state, no
financial state transfer is required.
