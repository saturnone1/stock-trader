# 0080 — Extract Trading Core as the single financial authority

- Status: Projection implementation accepted; production cutover paused
- Date: 2026-08-24
- Baseline: `e77fdac`
- Implementation: `d9f4f30`

## Context and measured trigger

Optimization, Market Data, and ML Training have passed their independent production service gates,
but live financial authority still runs inside the desktop-facing API. The same API process hosts
risk monitoring, position execution, pending-entry reconciliation, signal scanning, interactive
research, settings/account administration, reporting, and request handling.

The production baseline at selection time has one enabled and active Alpaca Paper account,
`AutoOrder` enabled, and five open positions totaling 566 shares. The API Pod used 24 millicores and
138 MiB at the sample point and had no restart since its current rollout. Current utilization does
not justify extraction for scaling. The trigger is release, failure, and secret isolation: an
ordinary API or research deployment currently restarts the loops responsible for protecting and
reconciling live positions, and the API Pod holds broker-order credentials even though most API
features do not need them.

Reporting and Notifications were evaluated first and deferred. All three production channels are
disabled and no credential material is configured, so another Pod would add recovery and delivery
semantics without solving a measured problem.

## Decision

Create one independent Trading Core service. It owns the complete strongly consistent financial
boundary:

- execution-intent acceptance and idempotent durable claim;
- risk state and the final pre-submission risk gate;
- broker order submission, cancellation, evidence polling, and reconciliation;
- positions, scaling executions, fills, and realized trades;
- accepted trade recommendations and their execution lifecycle;
- immutable financial activity events and cutover generation.

Risk, Order, Position, Fill, and Broker are modules inside this service, never separate network
services. A broker call may occur between durable request and evidence states, but reconciliation,
not a synchronous response alone, determines the canonical outcome.

The F# host is the concise transport, scheduling, health, and composition shell. Existing C# engine
and application policies remain the single implementation of risk and execution semantics and move
behind storage-neutral ports where necessary; formulas and trading catalogs are not translated into
F#. The service receives Market Data evidence through the accepted mTLS service contract and loads
only approved ML artifacts through their immutable manifest contract.

## State and API ownership

Trading Core receives its own SQLite volume and migration history initially. It never mounts or
opens the Control API database. Its canonical store contains execution intents, risk snapshots,
recommendations, broker-order attempts/evidence, positions, scaling executions, fills, trades,
activity outbox entries, inbox deduplication, account configuration generations, and the active
cutover generation.

Control API retains users, authentication, audit administration, strategy research, global UI
settings, and encrypted account administration. It exposes account configuration as a versioned
secret reference/generation over an authenticated control-plane contract; broker secret values are
delivered only to Trading Core and never appear in activity events, logs, or read projections.
After cutover, Control API reads Trading Core projections/contracts and cannot mutate the legacy
financial tables.

All commands carry schema version, command ID, correlation/causation IDs, account generation,
strategy artifact identity, Market Data evidence identity, occurred-at/expiry, and a canonical
payload hash. Re-delivery returns the original durable outcome; identity reuse with a different hash
fails closed. Activity events use a transactional outbox and stable financial business identity.

## Authority modes and migration

The adapter supports four explicit modes:

1. `Local` — the legacy API financial writer remains authoritative.
2. `Projection` — the candidate imports and continuously compares read-only state; it submits no
   broker command and emits no authoritative financial mutation.
3. `Shadow` — both paths evaluate the same evidence, but only Local may persist financial effects or
   contact the broker; decisions and rejection reasons are compared by stable semantic identity.
4. `Remote` — only Trading Core runs financial loops, contacts broker order endpoints, and owns the
   canonical store. The API rejects any local financial write path.

Migration preserves exactly one writer:

- back up the application database and create a point-in-time, hash-recorded import into the
  candidate store;
- start `Projection`, compare every financial row and read model, then run the complete conformance
  and recorded-decision corpus;
- run `Shadow` through market-open and closed cycles with broker submission physically disabled in
  the candidate;
- quiesce new intents, stop all legacy financial hosted loops, and reconcile every outstanding
  broker order/fill against the import watermark;
- record one monotonic cutover generation in both control and Trading Core audit records;
- enable `Remote`, release queued intents only under that generation, and continuously reconcile
  broker evidence and compatibility projections.

No dual write is permitted. Legacy financial tables become a rollback snapshot and compatibility
projection, not a second source of truth.

## Failure, rollback, and safety policy

Trading Core fails closed when account generation, risk state, strategy compatibility, model
manifest, Market Data evidence, calendar/cutoff, or broker reconciliation is missing, stale, or
ambiguous. Loss of the service prevents new orders; it cannot activate a Local fallback. Existing
broker orders and positions are recovered from durable intent plus broker evidence after restart.

Rollback first disables Remote intent acceptance, cancels or reconciles every in-flight broker
request, records a higher authority generation, and proves the candidate has no unresolved effect.
Only then may the reconciled canonical snapshot be imported and Local authority re-enabled. A mode
configuration change alone is never a financial-authority rollback.

Stop the cutover if two processes can reach order submission, any broker fill lacks one converged
durable identity, state repair requires editing both databases, or a semantic/version mismatch can
continue live execution.

## Deployment and working-set budget

The service has its own OCI image, ServiceAccount, Kubernetes Deployment, ClusterIP Service,
NetworkPolicy, resource requests/limits, health/readiness/metrics, mTLS identity, broker/control
secrets, SQLite/artifact volume, backup/restore path, and generation-scoped certificate rotation.
The supported deployment entry remains `scripts/deploy-k3s.sh`.

One replica is used while SQLite and broker authority are single-active. Multiple Pods require a
separate lease/fencing ADR and cannot imply infrastructure high availability on the one-node K3s
cluster.

The F# host starts below 200 nonblank lines per orchestration file. Financial policy remains in
shared C# engine/application assemblies; the service contains zero duplicated strategy, risk,
position, fill, timeframe, or broker-capability policy lines. The acceptance record will report
service-owned nonblank source lines, largest file, direct dependencies, and duplicated policy lines.

## Acceptance evidence required

This ADR becomes fully Accepted only when one service-level batch proves:

- independent image, Pod, identity, storage, broker-secret isolation, mTLS, probes, telemetry,
  resource limits, and no mount/read access to the application database;
- version/hash/account-generation rejection, authenticated authorization, duplicate/conflict,
  delay/reordering, inbox/outbox, and stable activity identities;
- full state import and projection parity for recommendations, open positions, scaling state,
  realized trades, pending orders, risk state, and read APIs;
- shared conformance results for future-bar exclusion, entry-bar consumption, conservative intrabar
  execution, cost timing, sizing/scaling/exits, calendar/DST, adjustment, and unsupported live
  features;
- Shadow decision parity with candidate broker submission impossible by both configuration and
  network/credential boundary;
- cutover fencing that proves only one order authority before, during, and after Remote activation;
- order submission ambiguity, duplicate response, timeout, delayed/out-of-order fill, cancellation,
  partial fill, service/API/Pod loss, and broker outage all converge to one durable outcome;
- load/resource isolation, backup/restore, TLS and workload-secret rotation/rollback, and a fully
  reconciled Remote-to-Local rollback rehearsal;
- API and desktop projections continue to work with legacy financial writes disabled.

Until every gate passes, Trading Core is incomplete and no Strategy Research/Edge extraction may
begin.

## Paused implementation checkpoint

The implementation batch at `d9f4f30` completed the independent service boundary, immutable
execution artifacts, entry and position financial lifecycle, durable inbox/outbox/intents, broker
reconciliation, canonical state/read contracts, authority fencing, encrypted account configuration,
mTLS deployment, and Projection import. The production API and Trading Core images were deployed in
`Projection` on 2026-08-24. The candidate received snapshots while financial intents and broker
evidence remained empty; its egress policy allowed DNS only.

This is an implementation checkpoint, not financial-authority acceptance. `Remote` remains
prohibited until the open acceptance gates above are completed, including full read-projection UI
cutover, manual-order immutable evidence, Shadow market-cycle parity, failure/partial-fill/broker
outage convergence, backup/restore and secret/TLS rollback drills, and a single-authority cutover
rehearsal. No later MSA extraction may start while this work is paused. Operational evidence and the
exact resume checklist are in the
[Trading Core Projection operations note](../../operations/trading-core-projection.md).

## Resumed read and command boundary checkpoint

The 2026-08-27 implementation batch completed the code-side Remote compatibility boundary without
changing production authority. In Remote mode the API now reads recommendation, position, trade,
risk, account, and dashboard financial state from Trading Core and every compatibility store is
read-only. Automatic and operator-selected entries carry a newly validated immutable execution
artifact and completed-bar evidence; AlertOnly recommendations use a separate non-broker command.
Position exits and scaling reuse the shared deterministic evaluator with the exact bars named by
the command evidence. Monotonic highest-price, stop, initial-risk, breakeven, and trailing state is
committed by Trading Core even when no broker order is due.

Trading Core continuously imports broker account and position evidence only in Remote, exposes
broker/canonical divergence in health and risk state, and blocks new entry preflight while an
unresolved financial intent or divergence exists. Accepted position commands durably mark their
canonical position pending; fills and terminal broker rejection clear that state, while ambiguous
or quantity-mismatched evidence remains reconciliation-required.

The complete local verification batch passed 1,010 backend tests, 75 desktop tests, API contract
generation, both independent builds, and the desktop production build. Production remains at the
`d9f4f30` Projection image pending a new deployment batch. The production application store had no
open position and no pending entry at the read-only 2026-08-27 audit; this observation is not a
cutover authorization. Shadow comparison, failure convergence, backup/restore, rotations, load,
and cutover/rollback gates remain mandatory.
