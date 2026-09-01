# 0082 — Define Trading Core acceptance and single-writer cutover

- Status: Proposed — design reviewed; awaiting user acceptance; implementation and Remote activation not authorized
- Date: 2026-09-01
- Extends: [ADR 0080](0080-extract-trading-core-service.md),
  [ADR 0081](0081-define-service-communication-topology.md)

## Context

Trading Core is deployed in production as comparison-only `Shadow` generation 2. Certificate-role
authorization, account-key generation migration and rollback, non-Remote database restore, Pod-loss
recovery, and one Alpaca Paper rejection probe have been exercised. The production account currently
has no open position, so no genuine completed-bar downtime replay or position-management comparison
can occur.

Creating a fake position or watermark in either production database would corrupt the evidence that
the cutover is intended to protect. Waiting indefinitely for a production position, however, mixes
the functional acceptance of the Pod with the business timing of one account. The acceptance design
therefore separates two kinds of evidence:

- isolated deterministic evidence proves failure convergence and autonomous completed-bar replay;
- genuine production Shadow observations prove compatibility with the deployed contracts, identities,
  calendars, data, account generation, and broker environment.

Neither class replaces the other. This ADR defines both and fixes the one-writer cutover and rollback
protocol before any further implementation or production action.

## Decision

### 1. Two-layer acceptance

Trading Core acceptance has two layers with different trust boundaries.

| Layer | Proves | Must not prove by assumption |
| --- | --- | --- |
| Isolated K3s acceptance | deterministic replay, idempotency, broker-failure convergence, restart recovery, load/resource bounds | production account or broker state |
| Production Shadow observation | deployed identity/contract compatibility, real Market Data evidence, Paper environment reachability, genuine decision parity | destructive failure injection or fabricated positions |

The isolated layer may close the functional completed-bar replay gate when production has no open
position. It cannot by itself authorize `Remote`. Production Shadow must still contain a non-empty,
identified decision corpus covering a market-closed rejection/no-action path and a market-open entry
or position path. If normal account activity does not produce the latter, a separately approved
Paper-only command may be used, but it must pass the normal Edge command boundary and may not insert
financial rows directly.

### 2. Isolated acceptance topology

Acceptance runs in a dedicated ephemeral namespace and never mounts a production volume or Secret:

```text
Acceptance driver
  |-- read-only mTLS --> production-compatible Market Data evidence endpoint
  |-- commands ------> isolated Trading Core candidate
  |                       |-- own temporary SQLite volume
  |                       +-- broker port --> deterministic scripted broker
  +-- kill/restart Pod, inspect durable outcome, collect immutable manifest
```

The candidate uses the same Trading Core worker, store, engine, contracts, migrations, and position
scheduler as production. A separate acceptance composition root binds a scripted broker adapter to
the existing purpose-specific broker port. Production composition binds only approved broker
adapters; no fault-injection endpoint, runtime switch, or scripted broker is included in the
production image.

The acceptance namespace receives only:

- a dedicated workload certificate authorized for Market Data evidence verification;
- no Alpaca, LS Securities, production account-encryption, or user authentication Secret;
- an ephemeral SQLite volume and explicit `acceptance-*` account, strategy, command, and authority
  identities;
- NetworkPolicy allowing DNS, the exact Market Data evidence route, and no public broker egress.

The driver obtains persisted completed bars through the real versioned evidence contract. It creates
an isolated position whose entry artifact, calendar, timeframe, evidence identity, and evaluated-bar
watermark are internally consistent with those bars. This is test state in an isolated store, never
a projection or migration into a production financial store.

### 3. Acceptance scenarios

One service-level batch executes the following matrix. Every retry reuses the original durable
command/client-order identity unless the previous command is conclusively terminal.

| Scenario | Required observation |
| --- | --- |
| completed-bar downtime | after Pod stop and later restart, every completed bar after the durable watermark is evaluated once, in order |
| duplicate delivery | identical command returns the original result; conflicting payload under the same identity fails closed |
| rejection before fill | durable terminal rejection, no position/fill, no blind retry |
| timeout before proven submission | command remains reconciling and lookup follows adapter policy |
| acceptance followed by timeout | lookup converges under the same client order ID; no second submission |
| delayed/out-of-order partial fills | cumulative broker evidence converges monotonically to one position/fill quantity |
| cancellation with partial fill | only broker-proven quantity is committed and the remainder is terminal |
| contradictory final quantity | authority becomes reconciliation-required; no new command crosses the broker boundary |
| duplicate broker response | inbox/evidence identity prevents duplicate financial effect |
| broker outage | existing intents remain durable, new unsafe work is fenced, recovery resumes reconciliation |
| Trading Core Pod loss | persistent intent and policy watermark survive and reconciliation starts within the objective |
| Edge loss | autonomous position protection continues; no user intent is invented |
| evidence correction | a correction touching an evaluated position range fences the position for reconciliation |
| accepted load | latency, SQLite busy time, queue age, CPU, memory, swap, and reconciliation cadence remain within the blueprint budget |

The earlier direct Alpaca Paper `422` probe is external broker compatibility evidence only. It does
not satisfy durable rejection, timeout, fill ordering, restart, or convergence scenarios.

### 4. Evidence manifest

The batch emits one immutable acceptance manifest rather than a collection of informal console
outputs. It records:

- source commit and immutable image digests for Edge, Trading Core, and Market Data;
- byte hashes of the shared Trading Core worker, store, engine, contracts, migrations, and position
  scheduler assemblies in both acceptance and production images; these hashes must match;
- contract/schema versions and engine compatibility version;
- acceptance namespace, workload identities, certificate generations, and NetworkPolicy digest;
- database migration version, pre/post integrity results, and backup/snapshot hashes;
- every scenario identity, injected broker script hash, expected terminal state, actual terminal
  state, and correlation/causation chain;
- evidence IDs, revisions, hashes, bar range, calendar, adjustment, timeframe, and final watermark;
- authority/account generations, unresolved-effect count, broker/canonical divergence, activity
  journal integrity, and any enabled consumer lag;
- resource samples and objective pass/fail results;
- operator identity and UTC start/completion times.

The manifest is valid only when all scenarios use the same candidate image digest. A changed binary,
contract, migration, broker adapter, scheduler, or financial policy invalidates the affected batch.

### 5. Remote cutover preconditions

Remote cutover may be scheduled only when all of the following are true at one recorded checkpoint:

1. the isolated acceptance manifest passes and is reviewed;
2. the production Shadow corpus is non-empty and has no unexplained semantic mismatch;
3. all production open positions have compatible immutable position-management artifacts and real
   evaluated-bar watermarks; zero open positions is also compatible;
4. broker reconciliation reports zero unresolved orders, fills, or quantity divergence;
5. Trading Core, Market Data, and Edge use compatible immutable image/contract versions;
6. certificate and account-encryption generations are active, decryptable, and preserved for the
   rollback window;
7. checked Edge and Trading Core backups exist, and their hashes and authority generations are in
   the cutover manifest;
8. Remote broker egress is restricted to validated HTTPS broker endpoints, while Shadow remains
   unable to reach them;
9. every Edge financial hosted service is absent from the Remote composition root;
10. the generation-monotonic rollback state machine has passed in the isolated namespace and the
    production operator procedure has completed a read-only dry run against the recorded inventory.

No precondition is waived because the market is closed or the account happens to have no position.

### 6. Single-writer cutover protocol

The cutover is a state machine, not simultaneous configuration changes:

```text
Stable(Shadow, Edge, g) -> Quiescing -> Reconciled -> Stable(Remote, TradingCore, g+1)
                                  any pre-commit stop -> Stable(Shadow, Edge, g+1)
```

1. **Freeze.** Record the cutover ID and reject creation of new automatic and manual financial
   intents at Edge. Read-only research remains available.
2. **Drain.** Allow already accepted Local commands to become terminal. Reconcile broker open
   orders, recent fills, canonical positions, quantities, and client-order IDs.
3. **Snapshot.** Stop Local financial schedulers, then create checked application and Trading Core
   backups plus the final import/projection watermark. No broker-capable candidate is active yet.
4. **Prove quiescence.** Require zero unresolved effect and inbox identity ambiguity, exact account
   generation, compatible open-position artifacts, matching broker/canonical hashes, and an
   integrity-valid activity journal. Undelivered journal rows are not ambiguity when no consumer is
   enabled.
5. **Advance authority.** Atomically write one higher monotonic generation and the cutover ID to the
   Trading Core canonical transition ledger, then idempotently mirror its receipt to Edge audit.
   Authority generation is never decremented or reused; this is not a distributed database
   transaction, and command acceptance remains fenced while the mirror converges.
6. **Start the new writer.** Activate Trading Core `Remote` first. It begins reconciliation and
   autonomous position protection but keeps new command acceptance fenced.
7. **Switch consumers.** Deploy Edge with all Local financial writers/schedulers absent and Remote
   reads/commands enabled. Prove from process registration, NetworkPolicy, credentials, and metrics
   that Edge cannot submit a broker order.
8. **Release.** Unfence Trading Core command acceptance for generation `g+1`, then observe at least
   one complete reconciliation cycle before declaring the cutover complete.

At no point may both workloads possess usable broker-order credentials and a registered order
submission path. Kubernetes readiness alone is not evidence of single-writer ownership.

### 7. Stop conditions

Freeze the transition and keep the current authority when any of these is observed:

- an open or ambiguous broker order, unprocessed fill, quantity mismatch, or stale reconciliation;
- a missing/incompatible strategy artifact, evidence revision, calendar, account, or authority
  generation;
- a Market Data correction covering an evaluated open-position range;
- an Edge financial writer or scheduler registered in the intended Remote process;
- more than one workload with both broker-order credentials and broker egress;
- database integrity failure, unmatched backup hash, activity-journal integrity failure, excessive
  lag for an enabled consumer, contract mismatch, or health dependency error;
- sustained swapping, OOM/restart, missed reconciliation interval, or acceptance objective failure;
- any need to edit both databases manually to proceed.

There is no automatic fallback to Local.

### 8. Reconciled Remote-to-Local rollback

The existing non-Remote restore script is not a Remote rollback mechanism. Remote rollback uses a
separate operator workflow and always advances authority:

```text
Stable(Remote, TradingCore, g) -> RollbackQuiescing -> Reconciled
                              -> Stable(Shadow, Edge, g+1)
```

1. fence new Remote commands while retaining Trading Core reconciliation;
2. reconcile every submitted client order ID, broker open order, fill, cancellation, position, and
   outbox event to a terminal or explicitly resolved state;
3. stop Remote financial scheduling and capture a checked canonical export plus broker-reconciliation
   hash;
4. import that export into the Local compatibility store while both financial writers are stopped;
5. verify row/business identities, quantities, realized results, policy watermarks, account
   generation, and read projections;
6. record Edge as the financial owner and Trading Core `Shadow` at generation `g+1` in the canonical
   transition ledger, then mirror its receipt to Edge audit (`Projection` is allowed only for a
   separately recorded Shadow-incompatibility reason);
7. start Edge Local financial services first with command acceptance fenced, then keep Trading Core
   in `Shadow` or `Projection` without broker credentials/egress;
8. prove exactly one Local broker authority, run one reconciliation cycle, then release commands.

If an in-flight broker effect cannot be resolved, rollback remains paused with Trading Core as the
only authority. Restoring an older database, toggling an environment variable, or starting Local as
an emergency fallback is forbidden.

### 9. Completion and rollback window

Trading Core Stage 5 is complete only after the isolated acceptance, genuine Shadow corpus, Remote
single-writer cutover, Remote Pod-loss recovery, and reconciled Remote-to-Local rehearsal all pass
against a named image set. After the rehearsal, the final intended authority is chosen explicitly;
the rehearsal itself never silently changes production authority.

Preserve the previous image digests, contract readers, certificates, encryption keys, checked
backups, and manifests until the reviewed rollback window closes. Real-money authority additionally
requires encrypted off-host backups and a storage-loss restore rehearsal, as specified by the MSA
blueprint.

### 10. Future implementation packages

The A0 language-neutral contract baseline is specified in
[Trading Core acceptance and authority contracts](../trading-core-acceptance-contracts.md). It keeps
the deployed v1 enum stable and models quiescing/reconciliation as transition phases rather than new
authority-mode values.
The A1/A2 topology, build separation, scripted broker protocol, controlled time, K3s isolation, and
manifest lifecycle are specified in
[Trading Core isolated acceptance design](../trading-core-isolated-acceptance-design.md).
The A3–A5 transition coordinator, canonical financial transfer, staged rollback import, and Remote
Edge capability proof are specified in
[Trading Core cutover coordination and Edge fencing design](../trading-core-cutover-coordination-design.md).
The A6/A7 immutable candidate, one-time verification, isolated K3s, production Shadow/cutover/
recovery/rollback, and final Remote evidence campaign are specified in
[Trading Core integrated candidate and acceptance plan](../trading-core-integration-and-acceptance-plan.md).
The cross-document consistency review, resolved findings, manageability boundaries, and compressed
implementation graph are recorded in
[Trading Core Stage 5 final design review](../trading-core-stage5-final-design-review.md).

Implementation follows the dependency graph below. These are design packages, not separately
deployable milestones:

```text
A0 Freeze acceptance contracts, manifest schema, authority states, and stop reasons
 |-- A1 Acceptance composition root + scripted broker adapter
 |-- A2 Ephemeral namespace, identity, NetworkPolicy, driver, and scenario catalog
 |-- A3 Quiesce/drain/fence authority coordinator
 |-- A4 Canonical export/import + reconciliation/hash verifier
 +-- A5 Edge Remote registration/credential absence proof
                    |
                    v
A6 Integrate one named image set and one acceptance manifest
                    |
                    v
A7 One full local gate -> one K3s isolated batch -> one production Shadow/cutover/rollback batch
```

A1–A5 may be developed independently against A0. None changes production authority, broadens
production egress, or triggers a production rollout by itself. A6 is the first completion candidate;
A7 is the only full verification and deployment wave.

## Consequences

- Functional replay and broker convergence no longer depend on waiting for a real production
  position, while production financial history remains untouched.
- A synthetic broker is kept out of the production binary and network surface.
- Remote activation stays intentionally manual, monotonic, and fail-closed.
- Acceptance costs one dedicated ephemeral workload/image, but it avoids adding a permanent broker,
  service mesh, or database and fits the single-node K3s design.
- Stage 6 remains blocked until this ADR's completion conditions are evidenced; this document alone
  authorizes no implementation, deployment, test execution, or authority change.
