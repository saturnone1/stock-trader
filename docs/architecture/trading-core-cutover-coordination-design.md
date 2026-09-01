# Trading Core cutover coordination and Edge fencing design

- Status: Proposed A3–A5 design baseline; no implementation, test run, deployment, or authority change authorized
- Parent contracts: [Trading Core acceptance and authority contracts](trading-core-acceptance-contracts.md)
- Parent decision: [ADR 0082](adr/0082-define-trading-core-acceptance-and-single-writer-cutover.md)
- Date: 2026-09-01

## 1. Goal and observed gaps

This document completes the design scope of ADR 0082 packages A3–A5:

- A3: a resumable authority-transition coordinator and command/scheduler fences;
- A4: canonical financial export, reconciliation, import, and rollback transfer;
- A5: proof that Remote Edge has no local financial writer or broker-submission capability.

The current Shadow implementation is a safe candidate but not a complete cutover mechanism:

- `/v1/authority` performs a one-shot mode update rather than a durable multi-phase transition;
- Edge hosted-service registration omits three financial loops in `Remote`, but the same process
  still registers Local financial implementations and provider-facing components;
- `TradingCoreProjectionService` combines account-configuration publication with legacy financial
  snapshot publication;
- the API Deployment always references the global Alpaca Secret, even when Market Data and Trading
  Core are Remote;
- the v1 snapshot omits the complete idempotency, terminal broker evidence, fill, activity cursor,
  and transfer manifest needed for a reconciled reverse migration;
- Edge and Trading Core databases cannot be atomically updated together.

The design resolves these without a permanent orchestrator service, distributed transaction,
shared database, or automatic fallback.

## 2. Component and ownership model

```text
Operator through scripts/deploy-k3s.sh
             |
             v
Short-lived Authority Transition Coordinator Job
  |-- mTLS control --> Edge transition boundary
  |-- mTLS control --> Trading Core canonical transition ledger
  |-- scoped K8s API --> exact Edge/Trading Core Deployments and Pods
  +-- no broker API, no Secret read, no database mount

Edge transition boundary
  durable command fences + scheduler barrier + source export + audit mirror

Trading Core transition boundary
  canonical epoch/phase ledger + target import/export + reconciliation + release
```

Trading Core remains the canonical epoch registry defined by A0. Edge owns its local fence and
financial-source export while it is the active owner. The Coordinator owns workflow progress only;
it does not own financial truth, derive a trading decision, or remain running after the transition.

## 3. Authority Transition Coordinator

### 3.1 Lifecycle and identity

The Coordinator is a short-lived F# Job created by an explicit future scope of the existing
`scripts/deploy-k3s.sh`. It receives one immutable `TransitionPlan` ConfigMap whose canonical hash is
recorded when the transition is requested. It resumes by `transitionId`; restarting the Job never
allocates another generation or repeats a non-idempotent operation.

Its mTLS role is `trading-cutover-coordinator.stocktrader.internal`. Regular Edge, Desktop,
acceptance-driver, and Trading Core workload certificates cannot call authority mutation or
financial-fence endpoints. The Coordinator certificate cannot submit trading commands or call a
broker.

The Job mounts no database, account Secret, broker Secret, production encryption key, or service
private key other than its own short-lived client identity. It may read status and redacted object
metadata but cannot read or list Kubernetes Secrets.

The Edge transition boundary listens on a dedicated internal mTLS port `5543` exposed as Service
port `3543`. It is not the public API port or the Optimization Worker port. Trading Core keeps its
existing internal `9443` listener and authorizes the Coordinator role only on v2 authority routes.

### 3.2 Scoped Kubernetes permissions

The Role is limited to the `stocktrader` namespace and exact named resources:

- get/watch the exact `stocktrader-api` and `stocktrader-trading-core` Deployments; Pod RBAC permits
  namespace-wide metadata reads because Kubernetes RBAC cannot restrict dynamic Pod names by label,
  while the Coordinator filters the two workload labels and has no logs, exec, delete, or patch
  permission on Pods;
- patch only those two Deployment specs to the immutable images and manifest hashes already named
  in the TransitionPlan;
- get the named ConfigMaps and NetworkPolicies required for capability attestation;
- get/watch the exact transfer/import Job pre-created by the operator-side deployment entry when
  offline staging is required;
- no Namespace, Secret, PVC, arbitrary Pod deletion, exec, port-forward, or cluster-scoped access.

The operator-side deployment entry creates the Job and immutable plan. The Coordinator cannot alter
its plan, select a different image, or widen its own Role.

### 3.3 Temporary network edges

The Coordinator uses default-deny networking with only these temporary edges:

| Source | Destination | Port | Purpose |
| --- | --- | ---: | --- |
| Coordinator | Edge internal cutover Service | 3543 | fence, barrier, export, mirror, capability receipt |
| Coordinator | Trading Core | 9443 | canonical transition, import/export, verification, release |
| Coordinator | `kubernetes.default` | 443 | exact scoped Deployment/Pod metadata and patch operations |
| Coordinator | kube-dns | 53 UDP/TCP | cluster DNS |

Edge and Trading Core authorize the exact Coordinator SAN per operation. The NetworkPolicy is
created with the transition Job and removed after the final manifest is sealed; its absence is part
of steady-state proof.

### 3.4 Transition plan

`TransitionPlanV1` contains:

- transition ID, direction, source/target modes and owners, source/reserved generations;
- exact Edge and Trading Core image digests and expected shared assembly hashes;
- exact Edge runtime profile, Trading Core runtime profile, and contract versions;
- expected TLS/encryption/account generations and named preserved rollback artifacts;
- expected Deployment, NetworkPolicy, ServiceAccount, and capability-inventory hashes;
- broker environment (`Paper` for the first rehearsal), reconciliation deadline, and transition
  expiry;
- accepted Shadow and isolated-acceptance manifest IDs;
- source/target transfer contract version and expected staging locations without credential values.

Changing any field after `Requested` requires aborting the transition and consuming the reserved
generation.

## 4. Edge financial fence and scheduler barrier

### 4.1 Durable fence state

Edge stores one `FinancialAuthorityFence` keyed by transition ID:

| Field | Meaning |
| --- | --- |
| `authorityGeneration` | generation under which Local work was accepted |
| `newEntryAcceptance` | `Open` or `Fenced` |
| `manualCommandAcceptance` | `Open` or `Fenced` |
| `positionCycle` | `Active`, `Finishing`, `AtBarrier`, or `Absent` |
| `entryReconciliation` | `Active`, `Draining`, `Clear`, or `Absent` |
| `positionReconciliation` | `Active`, `Draining`, `Clear`, or `Absent` |
| `lastCompletedPositionBarUtc` | final source-owned completed-bar watermark |
| `unresolvedIntentCount` / `unresolvedBrokerEffectCount` | exact financial-effect gate counts |
| `activityJournalCount` / `enabledConsumerLag` | journal integrity and lag; undelivered rows are allowed when no consumer exists |
| `fenceHash` | canonical hash returned to the Coordinator |

The fence is checked inside the application command boundary before any manual or automatic intent
is created. Endpoint-only or hosted-service-only checks are insufficient. Commands rejected during a
transition are not queued across generations; a research signal may be reevaluated after release
only with current evidence, expiry, risk, account, and authority generation.

### 4.2 Safe handoff of open-position protection

Quiescing happens in three ordered steps:

1. fence new entries and operator financial commands while Local remains owner;
2. allow the current Local position-protection and reconciliation cycles to finish;
3. at an explicit completed-bar barrier, persist all position policy state and prevent another Local
   position cycle from starting while reconciliation alone drains to zero.

This avoids abandoning an open position early or allowing a new Local position command after the
final transfer snapshot. Trading Core starts from the exported per-position watermark and replays
any later completed bars after it becomes the owner.

### 4.3 Edge transition operations

The internal v2 boundary exposes coordinator-only idempotent operations:

| Operation | Result |
| --- | --- |
| fence new commands | durable fence receipt; new entries/manual commands rejected |
| enter scheduler barrier | final position-cycle receipt and watermarks |
| read drain inventory | stable intents/orders/activity-journal/position state and counts |
| create source export | immutable transfer ID/hash from one database snapshot |
| attest Remote profile | runtime profile, hosted-service/DI/assembly/Secret-reference hashes |
| mirror authority receipt | idempotent audit copy of canonical Trading Core epoch |
| release Local after rollback | opens commands only after higher Edge-owned generation is verified |

These operations are not public user APIs and accept only the Coordinator role.

## 5. Canonical financial transfer contract

### 5.1 Transfer envelope

`CanonicalFinancialTransferV2` is direction-neutral and contains:

| Section | Required content |
| --- | --- |
| identity | transfer ID/hash, transition ID, direction, source owner/mode/generation, reserved target generation, captured UTC |
| compatibility | schema, engine, strategy artifact, pattern catalog, calendar, Market Data, and transfer versions |
| account reference | account IDs, broker/environment, active flags, configuration generation/hash; no credentials |
| recommendations | stable identities, source signals, immutable execution artifacts/evidence, execution linkage |
| positions | quantities/prices, execution context, scaling state, policy state, exact evaluated evidence revision/bar watermark |
| realized trades | stable trade/source identities, quantities, exact cost-adjusted financial values, entry/exit evidence |
| execution identities | every source signal, command ID, client order ID, broker order ID, terminal status, and payload hash needed to prevent replay |
| broker evidence summary | terminal order/fill/cancel evidence within the capability-defined safety window and every nonterminal identity regardless of age |
| risk state | daily boundary, equity basis, halt state, observation timestamp, account generation |
| activity continuity | last aggregate versions, journal high watermark and per-enabled-consumer cursor/lag summary |
| section manifest | ordered row counts and hashes for every section plus overall transfer hash |

Financial decimal values use invariant exact decimal strings with named units. Quantities,
generations, and revisions are integers. Collections are ordered by stable business identity before
hashing. Current market price is excluded from broker/canonical quantity reconciliation but may be
carried as explicitly non-authoritative display evidence.

### 5.2 Snapshot consistency

The source creates the transfer while command acceptance and position scheduling are fenced:

1. record broker evidence observation `B1`;
2. open one consistent read transaction and capture all source financial sections at `D`;
3. record broker evidence observation `B2` using the same account and capability-defined evidence
   window;
4. require identical open-order/fill/position identities and quantities across `B1`, `D`, and `B2`;
5. require zero nonterminal command, broker ambiguity, or quantity divergence, plus activity-journal
   integrity and acceptable lag for every enabled consumer;
6. seal the transfer and its reconciliation hash.

The broker evidence window is owned by the broker capability catalog and must cover every unresolved
client identity plus a configured safety overlap. A hard-coded seven-day query is not a transfer
invariant.

### 5.3 Reconciliation hash

The reconciliation hash covers normalized:

- account generation and pseudonymous account identity;
- symbol and signed broker/canonical quantity;
- client order ID, broker order ID, side, requested quantity, cumulative fill quantity, terminal
  status, and last broker evidence UTC;
- canonical position and terminal execution identity;
- source snapshot ID and final per-position completed-bar watermark.

It excludes transient current price, display PnL, provider response ordering, and free-form broker
messages. A mismatch returns a stable A0 stop reason and cannot be operator-overridden.

## 6. Import and staging protocol

### 6.1 Cutover: Edge to Trading Core

Trading Core imports the sealed transfer into its existing candidate database while both command
paths are fenced. One transaction validates compatibility, transfer hash, account generation,
duplicate identities, execution artifacts, evidence revisions, open-position protection, section
counts/hashes, and zero unresolved effects before replacing candidate canonical financial tables.

The import records `transferId + reservedGeneration` as an idempotency identity. Repeating the same
payload returns the original receipt; another payload for that identity fails. Projection rows remain
an audit/rollback source during the rollback window but are never a second writable authority.

### 6.2 Rollback: Trading Core to Edge

Rollback never restores an old whole application database because that would discard nonfinancial
users, strategies, settings, and research changes made after cutover. Instead:

1. fence Trading Core commands and reconcile all broker effects;
2. create a checked whole-file backup of the current Edge database and a sealed Trading Core
   financial transfer;
3. stop the Edge API and Local financial services while Trading Core remains the fenced owner;
4. copy the current Edge database to a staging file;
5. replace only the defined legacy financial compatibility tables in one staging transaction;
6. retain nonfinancial tables unchanged and append transfer/audit identities;
7. run integrity, row/hash, relationship, projection, and generation checks against staging;
8. atomically replace the stopped Edge database with staging while preserving the pre-import file;
9. record the higher Edge-owned generation in the Trading Core ledger and mirror it to Edge;
10. deploy the preserved Local-capable Edge image with commands fenced, demote Trading Core to
    `Shadow`, prove capability ownership, reconcile once, then release Local.

The existing non-Remote Trading Core restore script is not used for this flow. Import failure leaves
the live Edge database untouched and Trading Core as the only fenced owner.

### 6.3 Import ownership and schema mapping

One versioned mapper owns each transfer section in each direction. Endpoint code, deployment scripts,
and EF entities do not define mappings. Unknown destination schema or unmapped required field fails
before mutation. Compatibility readers remain through the rollback window; destructive removal of
legacy financial tables requires a later ADR.

## 7. Edge runtime profiles

### 7.1 Separate immutable images

The same commit produces two Edge images:

| Image | Purpose | Included financial capability |
| --- | --- | --- |
| `api-local` | Local/Projection/Shadow and preserved rollback | legacy Local financial module and approved provider adapters |
| `api-remote` | post-cutover Edge | remote Trading Core clients, account administration/publication, research command producers; no Local broker adapter or writer module |

This adds no running Pod. It reduces the code and credential surface of the one existing API Pod.
Common endpoints/application modules compile once into shared assemblies. Provider/broker adapters
and Local financial orchestration live in a local-only assembly that `api-remote` does not reference
or copy.

The preserved `api-local` digest cannot be deployed while Trading Core is the active owner. A
rollback transition names that digest in advance and keeps it fenced until the higher Edge-owned
generation is committed.

### 7.2 Remote composition inventory

Remote Edge explicitly includes:

- user authentication, strategy research, preview/backtest/optimization orchestration;
- Pattern Scanner as a Trading Core command producer, never a broker caller;
- Trading Core command/read/risk/order-management clients;
- a purpose-specific account-configuration publisher;
- reporting/read consumers, ML publication reconciliation, and nonfinancial research ingestion.

Remote Edge explicitly excludes:

- `RiskMonitorService`, `EntryExecutionReconciliationService`, and
  `PositionExecutionManagerService`;
- Local entry/position execution coordinators, Local order management, broker factories, broker SDK
  adapters, and any service that can call a broker order/cancel/history operation;
- `TradingCoreProjectionService` financial snapshot publication and
  `TradingCorePositionShadowService`;
- Alpaca streaming, Market Data subscription/sync/backfill/ingestion/daily-data workers already
  owned by the Remote Market Data service;
- global Alpaca/LS broker Secret environment or volume references.

The current combined projection worker separates into an account-configuration publisher retained
in Remote and a legacy financial snapshot publisher available only before cutover. Account
administration may decrypt credentials transiently to publish the versioned configuration to
Trading Core, but the Remote image has no broker adapter/factory capable of using them.

### 7.3 Legacy financial write guard

In `api-remote`, application ports for position, trade, recommendation execution, risk mutation, and
broker reconciliation bind only to remote clients or read projections. A SaveChanges guard rejects
any tracked mutation of the legacy financial entity catalog while the Remote profile is active.
Offline rollback import operates against a stopped staging database and is the only designed bypass;
there is no runtime configuration flag to disable the guard.

## 8. Capability attestation

Single-writer proof combines independent evidence rather than trusting one status response.

### 8.1 Edge proof

The Coordinator verifies:

- `api-remote` immutable digest and startup runtime-profile hash;
- image/SBOM absence of the local-only financial/broker adapter assemblies and broker SDK packages;
- DI inventory absence of broker factories, Local financial writers, and forbidden hosted services;
- Deployment absence of global broker Secret references;
- persistent Edge command fence and legacy-financial SaveChanges guard;
- source reconciliation counts are zero and the authority mirror matches the canonical epoch.

### 8.2 Trading Core proof

The Coordinator verifies:

- exact Trading Core digest, account/encryption/TLS generations, and canonical authority epoch;
- approved broker adapter inventory and validated Paper endpoint for the first rehearsal;
- Remote-only broker egress policy digest and Market Data evidence role;
- command acceptance remains fenced until import, reconciliation, and one autonomous protection
  cycle complete;
- no unresolved intent, broker effect, or divergence; activity journal integrity passes and every
  enabled consumer remains within its declared lag bound.

### 8.3 Cluster proof

The Coordinator reads redacted Deployment/Pod/NetworkPolicy metadata and proves that only Trading
Core has the complete broker capability tuple from A0. Self-reported DI inventory, Kubernetes
configuration, and image contents must agree; disagreement blocks release.

## 9. End-to-end cutover sequence

```text
P0 Preflight accepted manifests, versions, artifacts, backups, zero stop reasons
P1 Create transition; reserve g+1; fence Trading Core and Edge entries/manual commands
P2 Finish Local position cycle; enter scheduler barrier; drain Local reconciliation
P3 Capture B1/D/B2; seal Edge -> Trading Core transfer and reconciliation hash
P4 Import candidate; verify canonical projections and autonomous replay compatibility
P5 Commit Remote g+1 in canonical ledger while both command paths remain fenced
P6 Deploy api-remote first, proving Edge adapter/Secret/writer absence
P7 Activate Trading Core Remote capability and restricted broker egress
P8 Reconcile broker and run one autonomous position-protection cycle
P9 Attest single writer; complete transition; explicitly release Trading Core commands
P10 Seal ProductionCutover manifest and preserve rollback artifacts
```

P6 precedes P7 so the old broker-capable Edge image is gone before Trading Core receives broker
capability. The epoch is already committed at P5, but both command paths remain fenced, so this
ordering trades brief unavailability for proof against dual submission.

## 10. End-to-end rollback sequence

```text
R0 Create rollback transition from Remote g; reserve g+1; fence Trading Core commands
R1 Keep Trading Core reconciliation/protection active until a safe scheduler barrier
R2 Reconcile B1/D/B2 and seal Trading Core -> Edge transfer
R3 Stop Edge; import into a staging copy; verify and atomically replace Edge database
R4 Remove Trading Core broker egress/capability while it remains the Remote owner, still fenced
R5 Commit Edge-owned Shadow g+1 in canonical ledger, apply the Shadow profile, and mirror receipt
R6 Deploy preserved api-local with commands fenced and exact broker Secret references
R7 Reconcile once, attest only Edge has broker capability, then release Local commands
R8 Seal ProductionRollback manifest and explicitly choose the intended final authority
```

If a broker effect cannot converge after R0, Trading Core remains the fenced owner and only
reconciliation/protection continues. The Coordinator cannot start `api-local` as an emergency
fallback.

## 11. Crash and ambiguity recovery

Every Coordinator operation is idempotent and phase-checked. On restart it reads the canonical
transition aggregate, Edge fence receipt, K8s capability inventory, and transfer/import receipts,
then resumes the first incomplete operation.

| Crash point | Recovery rule |
| --- | --- |
| before `Requested` receipt | retry same operation ID or prove no transition exists |
| after fencing one side | fence the other; never release the first |
| during drain/export | Local remains owner and fenced; recreate export with same transfer identity only if no sealed payload exists |
| after sealed export, before import | reuse exact transfer hash |
| after import, before epoch commit | verify import receipt; abort may retain source at reserved generation |
| after epoch commit | target is owner but fenced; rollback requires a new reverse transition, never abort |
| during profile rollout | inspect actual image/capability inventory and continue; do not infer from desired Deployment state |
| after release, before manifest seal | keep authority; reconstruct evidence from durable receipts and fail manifest if incomplete |

No recovery step edits both databases manually or changes an epoch based only on an environment
variable.

## 12. Observability and operator view

The operator sees one transition view containing phase, source/target owner and mode, current and
reserved generation, command fences, scheduler barrier, transfer/import hashes, broker
reconciliation age/hash, unresolved counts, actual image/profile/capability inventory, active stop
reasons, and the last idempotent operation receipt.

Metrics use stable phase/reason labels but never account IDs, symbols, credentials, or payloads.
Transition logs include operation/correlation IDs and hashes; exception text remains diagnostic and
cannot drive automation.

## 13. Future implementation packages

The future implementation is prepared in parallel against A0 contracts:

```text
A3.1 Coordinator identity, immutable plan, scoped RBAC, and resumable operation client
A3.2 Edge durable fence, scheduler barrier, drain inventory, and internal control boundary
A4.1 Direction-neutral transfer contract, section hashers, and reconciliation normalizer
A4.2 Edge exporter/staging importer + Trading Core importer/exporter
A5.1 Split account publisher from legacy projection publisher
A5.2 Local-only financial/provider adapter assembly + api-local/api-remote hosts
A5.3 Runtime/DI/SBOM/Secret/NetworkPolicy capability attestation
                              |
                              v
A5.4 Integrate one cutover/rollback candidate and perform static plan inspection
                              |
                              v
Later A6/A7 only: one full build/test gate and one production acceptance batch
```

None of A3.1–A5.3 is separately deployed or counted as a completed Pod milestone. The integrated
candidate is the first unit eligible for the full verification sequence.

## 14. A3–A5 design exit criteria

A3–A5 are ready for review when:

- only a dedicated short-lived identity can mutate authority or financial fences;
- transition progress survives Coordinator loss without another generation or duplicate effect;
- entries, manual commands, position scheduling, and reconciliation have separate explicit fences;
- one direction-neutral transfer preserves idempotency, execution, policy-watermark, risk, and
  activity continuity without credentials;
- rollback updates only financial compatibility state in a staging copy of the current Edge DB;
- `api-remote` lacks Local broker/writer assemblies, registrations, hosted services, and Secret
  references while retaining account publication and remote command/read behavior;
- capability attestation combines image, process, Kubernetes, ledger, and broker-reconciliation
  evidence;
- cutover and rollback never grant broker capability before removing it from the previous owner;
- every ambiguous crash point has one deterministic resume rule;
- full testing and deployment remain deferred to the integrated A6/A7 batch.

Until this design is accepted, no Coordinator, transfer/import path, Edge image split, deployment
profile, Secret change, or authority transition is authorized.

The proposed A6/A7 integration and evidence consumer is documented in
[Trading Core integrated candidate and acceptance plan](trading-core-integration-and-acceptance-plan.md).
