# Trading Core acceptance and authority contracts

- Status: Proposed A0 design baseline; no implementation or authority change authorized
- Contract family: `trading-core-control/v2`, `trading-core-acceptance-manifest/v1`
- Parent decision: [ADR 0082](adr/0082-define-trading-core-acceptance-and-single-writer-cutover.md)
- Date: 2026-09-01

## 1. Purpose and scope

This document freezes the language-neutral contracts needed to implement ADR 0082 without deciding
authority, retry, or evidence semantics inside an HTTP handler or deployment script. It defines:

- the difference between the active financial owner and Trading Core's operating mode;
- the durable transition aggregate and legal state changes;
- stable stop-reason and scenario catalogs;
- the acceptance-manifest shape and pass calculation;
- control-plane operations, idempotency, compatibility, hashing, and storage ownership.

It does not add endpoints, database tables, Kubernetes objects, test fixtures, or scripts. Existing
`trading-core/v1` remains the deployed Shadow contract until a separately reviewed implementation
batch completes.

## 2. Existing v1 compatibility boundary

The existing `TradingAuthorityMode` numeric and JSON identities remain unchanged:

| v1 mode | Financial owner | Trading Core behavior |
| --- | --- | --- |
| `Local` | Edge | disabled/local compatibility |
| `Projection` | Edge | import and expose read-only projections |
| `Shadow` | Edge | projection plus comparison; no broker mutation |
| `Remote` | Trading Core | canonical financial writer and broker authority |

`Quiescing`, `Reconciled`, and rollback phases are not new values of this enum. They describe an
authority transition while one of the four modes remains the active durable mode. This avoids enum
renumbering, unknown-mode behavior, and a state in which neither database can identify the owner.

The v1 `/v1/authority` one-shot transition may remain available only while v2 consumers are being
deployed. It must be rejected once a v2 transition row exists or the compatibility window closes.
No script may compose a v2 transition from multiple calls to the v1 endpoint.

## 3. Stable identities and canonical representation

Every control object carries these common fields:

| Field | Rule |
| --- | --- |
| `contractVersion` | positive integer; exact supported version required for mutations |
| `operationId` | caller-generated UUID, stable across retries |
| `payloadHash` | SHA-256 of the canonical payload excluding only `payloadHash` |
| `correlationId` | stable for the operator workflow |
| `causationId` | preceding operation ID, absent only for the root |
| `observedAtUtc` | UTC timestamp supplied by the observing owner, never server-local time |

Control and manifest hashes use **StockTrader Canonical JSON v1**:

- UTF-8 without BOM;
- camel-case property names with no aliases;
- object properties recursively sorted by ordinal name;
- arrays retained in contract-defined order;
- required properties always emitted, including explicit `null` where allowed;
- UTC timestamps formatted as RFC 3339 with seven fractional digits and `Z`;
- digests encoded as uppercase hexadecimal SHA-256;
- quantities, generations, byte counts, milliseconds, and millicores encoded as integers;
- no floating-point values in authority or manifest contracts;
- any financial decimal included by reference is identified by the existing evidence/artifact hash,
  not reformatted into this control contract.

These restrictions deliberately avoid the cross-language enum and decimal representation failures
previously observed during service integration.

## 4. Authority transition aggregate

### 4.1 Durable state

One `AuthorityTransition` is the sole coordinator for a proposed ownership change:

| Field | Meaning |
| --- | --- |
| `transitionId` | stable UUID and idempotency identity |
| `direction` | `Cutover` or `Rollback` |
| `sourceMode` / `targetMode` | exact v1 operating modes |
| `sourceOwner` / `targetOwner` | `Edge` or `TradingCore`, derived and validated from mode |
| `sourceGeneration` | active generation at creation |
| `reservedGeneration` | exactly `sourceGeneration + 1`; never reused |
| `phase` | current transition phase from the catalog below |
| `commandAcceptance` | `Open` or `Fenced` |
| `sourceStateHash` | hash of the source financial snapshot/import watermark |
| `brokerReconciliationHash` | hash of broker/canonical comparison evidence |
| `accountGeneration` | exact encrypted account configuration generation |
| `startedAtUtc` / `expiresAtUtc` | bounded operator window |
| `lastOperationId` | optimistic concurrency and audit identity |
| `outcome` | `None`, `TargetCommitted`, or `SourceRetained`; set only by epoch commit or pre-commit abort |
| `stopReasons` | stable reason codes active on the aggregate |

Only one non-terminal transition may exist. Its payload hash and every phase operation are stored in
the same database transaction as the transition audit event.

### 4.2 Phase catalog

| Phase | Active owner | Command acceptance | Allowed next phase |
| --- | --- | --- | --- |
| `Requested` | source | fenced before acknowledgement | `Quiescing`, abort to `ReadyToRelease`, `Blocked` |
| `Quiescing` | source | fenced | `Draining`, abort to `ReadyToRelease`, `Blocked` |
| `Draining` | source | fenced | `Reconciled`, abort to `ReadyToRelease`, `Blocked` |
| `Reconciled` | source | fenced | `Committing`, abort to `ReadyToRelease`, `Blocked` |
| `Committing` | source until atomic epoch commit | fenced | `Verifying`, `Blocked` |
| `Verifying` | target at reserved generation | fenced | `ReadyToRelease`, `Blocked` |
| `ReadyToRelease` | target for `TargetCommitted`; source for `SourceRetained` | fenced | `Completed`, `Blocked` |
| `Completed` | owner identified by outcome at reserved generation | open | terminal |
| `Blocked` | owner determined by durable epoch-commit marker | fenced | before commit: retain source into `ReadyToRelease` or resume; after commit: resume verification |

A pre-commit abort always consumes `reservedGeneration`, republishes the source mode at the higher
generation, sets outcome `SourceRetained`, and enters `ReadyToRelease`. After epoch commit, reversal
is not an abort and must start a new transition. This makes every command created before a failed
attempt stale without decrementing an epoch.

### 4.3 Legal direction and owner mapping

The initial production transitions are deliberately narrow:

| Direction | Source | Target | Source owner | Target owner |
| --- | --- | --- | --- | --- |
| Cutover | `Shadow` | `Remote` | Edge | Trading Core |
| Rollback | `Remote` | `Shadow` | Trading Core | Edge |

`Projection` may replace the rollback target only if Shadow comparison is incompatible and the
reviewed transition request records that reason. `Local -> Remote`, `Projection -> Remote`, and any
transition that skips reconciliation are invalid. A transition does not change the authority mode
stored in either service until the atomic epoch-commit operation.

### 4.4 Ownership invariant

At every phase exactly one side is the financial owner. Broker submission additionally requires all
four capabilities in the same workload:

```text
active authority generation
+ command acceptance Open
+ registered broker submission adapter
+ broker-order Secret and permitted egress
```

Absence of any capability blocks submission. The manifest must prove that the non-owner lacks at
least the adapter and broker Secret/egress; a configuration string alone is insufficient.

### 4.5 Canonical ledger and cross-store consistency

The Trading Core transition ledger is the canonical epoch registry. Edge stores a read-only
authority mirror, its own durable command-fence acknowledgement, and audit receipts; it never mints
or edits an authority generation independently.

There is no distributed database transaction between Edge and Trading Core. Safety comes from this
ordering:

1. both sides durably fence command acceptance and return hashed acknowledgements;
2. Trading Core records those acknowledgements and atomically commits the target epoch while both
   sides remain fenced;
3. deployment removes the source's broker adapter/Secret/egress and establishes them only for the
   target;
4. both sides report the committed epoch and capability inventory;
5. only the target receives an explicit release operation.

If an acknowledgement or mirror update is lost, retry uses the same operation identity. If the two
stores disagree, the higher canonical ledger epoch wins for fencing purposes, but no command is
released until the mirror and capability inventory match. An Edge outage before a transition does
not stop normal Local ownership; after `Requested`, the persisted Edge fence is mandatory and a
restarted old Edge image cannot receive broker capability.

## 5. Control-plane operation contracts

The future v2 API is command-oriented. Repeating the same `operationId` and payload returns the
original receipt; reusing an ID with another hash returns `operation-identity-conflict`.

| Operation | Purpose | Required phase/result |
| --- | --- | --- |
| `POST /v2/authority/transitions` | reserve generation and fence acceptance | creates `Requested` |
| `POST /v2/authority/transitions/{id}/quiesce` | attest Edge/Trading Core command fences | `Quiescing` |
| `POST /v2/authority/transitions/{id}/drain` | attach open-order/intent/activity-journal inventory | `Draining` |
| `PUT /v2/authority/transitions/{id}/reconciliation` | attach snapshot and broker reconciliation hashes | `Reconciled` only if every gate passes |
| `POST /v2/authority/transitions/{id}/commit` | atomically publish target mode and reserved generation | `Committing -> Verifying` |
| `POST /v2/authority/transitions/{id}/complete-verification` | attach ownership proof after one healthy reconciliation cycle | `ReadyToRelease`, still fenced |
| `POST /v2/authority/transitions/{id}/release` | open the effective owner's commands | `Completed`; does not alter generation |
| `POST /v2/authority/transitions/{id}/abort` | consume reserved generation and retain source owner before commit | `ReadyToRelease` with `SourceRetained`, still fenced |
| `GET /v2/authority` | return active mode/owner/generation and acceptance state | read-only |
| `GET /v2/authority/transitions/{id}` | return the durable aggregate and stop reasons | read-only |

`complete-verification` and `release` are separate so a successful deployment cannot accept new orders before
the first target-owned reconciliation cycle. Authority mutation accepts only the dedicated
short-lived Coordinator certificate role. The normal Edge control role may read authority and
submit its own fence/audit acknowledgement but cannot mint or commit a generation. An acceptance
driver uses a separate role and cannot call production authority mutations.

## 6. Stop-reason contract

Stop reasons are stable machine codes, not exception messages. Each carries `code`, `category`,
`requiredAction`, `firstObservedAtUtc`, `lastObservedAtUtc`, and evidence references.

### 6.1 Required-action values

| Value | Meaning |
| --- | --- |
| `BlockStart` | precondition is not met; no transition may be created |
| `FenceAndPause` | retain current owner and keep commands fenced |
| `AbortBeforeCommit` | consume reserved generation and retain source owner |
| `ReconcileOnly` | target remains owner; only reconciliation may continue |
| `RejectManifest` | acceptance run is invalid and must be repeated |

### 6.2 Stable code catalog

| Category | Codes |
| --- | --- |
| Identity | `caller-role-unauthorized`, `certificate-generation-mismatch`, `acceptance-production-secret-present` |
| Contract | `unsupported-contract`, `payload-hash-mismatch`, `operation-identity-conflict`, `image-assembly-hash-mismatch` |
| Authority | `transition-already-active`, `authority-generation-mismatch`, `illegal-authority-transition`, `dual-broker-capability`, `edge-financial-writer-present`, `command-fence-not-proven` |
| Financial | `unresolved-financial-intent`, `unresolved-broker-order`, `unprocessed-broker-fill`, `broker-canonical-quantity-divergence`, `stale-broker-reconciliation`, `activity-journal-integrity-failed`, `activity-consumer-lag-exceeded` |
| Evidence | `market-evidence-missing`, `market-evidence-incomplete`, `market-evidence-corrected`, `open-position-protection-incompatible`, `strategy-artifact-incompatible` |
| Persistence | `database-integrity-failed`, `backup-hash-mismatch`, `snapshot-hash-mismatch`, `canonical-import-mismatch`, `manual-dual-store-edit-required` |
| Shadow | `shadow-corpus-empty`, `shadow-semantic-mismatch`, `required-market-window-unobserved` |
| Dependency | `market-data-unready`, `broker-environment-invalid`, `broker-outage`, `service-readiness-failed` |
| Resource | `resource-objective-failed`, `swap-pressure-detected`, `oom-or-restart-detected`, `reconciliation-cadence-missed` |

New codes are additive within v2. Renaming or changing the meaning/action of a code requires a new
contract version. Human detail may be added to logs or UI but cannot drive automation.

HTTP mapping is fixed: malformed shape/hash `400`, unauthenticated `401`, wrong role `403`, identity
or transition conflict `409`, unmet safety gate `412`, semantically invalid evidence `422`, and a
currently unavailable required dependency `503`.

## 7. Acceptance scenario contract

Every result has a stable `scenarioId`, catalog `scenarioCode`, `fixtureHash`, `expectedStateHash`,
`actualStateHash`, ordered operation/evidence references, resource-sample references, start/end UTC,
and `Passed` or `Failed`. The required v1 codes are:

```text
completed-bar-downtime-replay
duplicate-command-delivery
command-identity-conflict
broker-rejection-before-fill
broker-timeout-before-submission-proof
broker-accepted-then-timeout
delayed-out-of-order-partial-fills
cancellation-with-partial-fill
contradictory-terminal-quantity
duplicate-broker-response
broker-outage-and-recovery
trading-core-pod-loss
edge-loss-autonomous-protection
evaluated-range-evidence-correction
accepted-resource-load
isolated-cutover-and-rollback-generation
```

A result is not passable by free-form operator override. An expected stop reason counts as success
only when the scenario definition names that exact reason and proves no forbidden financial effect.

## 8. Acceptance manifest contract

### 8.1 Top-level shape

`AcceptanceManifestV1` contains these required sections:

| Section | Required content |
| --- | --- |
| Identity | `manifestId`, schema version, run ID, environment class, start/end UTC, operator/correlation IDs |
| Source set | repository commit, build ID, image names and immutable digests |
| Assembly set | shared worker/store/engine/contracts/migration/scheduler assembly names and byte hashes for acceptance and production images |
| Contract set | Trading Core, Market Data, engine, strategy artifact, calendar, and manifest versions |
| Topology | namespace, ServiceAccounts, certificate role/generation, NetworkPolicy digest, volume class, broker egress classification |
| Initial state | mode, owner, generation, account generation, snapshot, broker reconciliation, unresolved counts |
| Evidence set | evidence IDs/revisions/hashes, symbol, timeframe, adjustment, calendar, bar range and watermark |
| Scenario results | every required scenario code exactly once, plus explicitly versioned optional scenarios |
| Resource results | integer CPU/memory/latency/queue/SQLite/reconciliation samples and objective result |
| Recovery artifacts | database/export hashes, encryption and TLS generations, off-host status without secret/path disclosure |
| Final state | mode, owner, generation, command fence, unresolved counts, integrity and reconciliation hashes |
| Verdict | derived result and ordered stop reasons |

`manifestId` is the canonical hash of the complete manifest with only `manifestId` excluded. Raw
credentials, ciphertext, certificate private material, cookies, authorization headers, full account
identifiers, absolute secret paths, and broker response bodies are forbidden.

### 8.2 Environment classes

Allowed values are `IsolatedAcceptance`, `ProductionShadow`, `ProductionCutover`, `RemoteRecovery`,
`ProductionRollback`, and `FinalRemote`. A complete Stage 5 evidence set contains at least one
passing manifest of each class against the named compatible image set. Isolated evidence cannot be
relabeled as production, and the first cutover cannot substitute for the final recutover state.

### 8.3 Derived verdict

The manifest verdict is `Passed` only when:

1. every required scenario for its environment class appears exactly once and passes;
2. all acceptance/production shared assembly hashes match;
3. no `RejectManifest`, `FenceAndPause`, or `ReconcileOnly` stop reason remains active;
4. initial and final authority generations obey the transition rules;
5. unresolved broker/order/fill/divergence counts are zero where the scenario expects convergence;
6. database integrity, snapshot, reconciliation, and backup hashes are present and consistent;
7. all resource objectives pass without swapping, OOM, or a missed reconciliation interval.

The verifier computes this verdict. The producer cannot submit `Passed` as an independent fact.

## 9. Manifest ownership and retention

The ephemeral acceptance namespace creates the manifest but is not its durable owner. The Control
API audit boundary records the manifest ID, environment class, image set, verdict, and off-cluster
artifact location. The complete redacted JSON is written to the existing operations backup boundary
and copied off-host before real-money authority; Trading Core stores only the manifest ID associated
with an authority transition.

This avoids adding an artifact service or shared database. A missing artifact, mismatched hash, or
on-node-only real-money recovery artifact is a failed gate. Retention lasts through the reviewed
rollback window and any audit period required for the trading account.

### 9.1 Transactional activity journal semantics

The current table named `outbox` is treated as the transactional activity journal until a real
consumer exists. An undelivered row is not an unresolved broker or financial effect. Stage 5 gates
its row identity/hash continuity, aggregate-version uniqueness, transfer high watermark, database
integrity, and storage growth—not a universal pending count of zero.

Future Reporting/Notification consumers receive independent durable cursor/inbox state. Only the lag
of an enabled consumer is a gate, and one consumer never marks an event delivered for another.
Journal rows are not deleted during the rollback window or while referenced by a transfer/manifest.
Any later archive/retention mechanism must preserve segment hashes and requires its own reviewed
policy; Stage 5 stops on filesystem reserve breach rather than silently deleting audit evidence.

## 10. Compatibility and rollout rules

1. Add v2 readers and durable transition storage while v1 Shadow remains authoritative only at
   Edge; do not expose Remote broker capability.
2. Edge and Trading Core advertise `supportedControlVersions`; mutations choose one exact version,
   never best-effort field fallback.
3. Deploy consumers that understand v2 failure responses before enabling v2 producers.
4. Once any v2 transition is recorded, v1 authority mutation is permanently fenced for that store.
5. Unknown phase, reason, scenario, environment, or manifest version fails closed for mutation and
   remains displayable as opaque data for audit.
6. A source/image/contract/assembly change after acceptance invalidates only the affected evidence,
   but any financial-policy or scheduler change requires the complete acceptance batch again.
7. No implementation package changes authority. Only the integrated candidate and reviewed
   production transition may do so.

## 11. A0 exit criteria

A0 design is ready for review when:

- every authority term has one meaning and maps to the existing v1 mode;
- every transition reserves and consumes exactly one higher generation;
- command release is distinct from authority commit;
- every automated stop condition has a stable code and action;
- manifest pass/fail is derived and cannot be asserted by the producer;
- secrets and production financial identities are excluded from acceptance artifacts;
- isolated and production evidence cannot substitute for each other;
- A1–A5 can implement against these contracts without defining new authority semantics.

Until this document is accepted, A1–A5 may be discussed but must not be implemented.

The proposed A1/A2 consumer of these contracts is documented in
[Trading Core isolated acceptance design](trading-core-isolated-acceptance-design.md).
The proposed A3–A5 authority, transfer, and Edge capability consumer is documented in
[Trading Core cutover coordination and Edge fencing design](trading-core-cutover-coordination-design.md).
The proposed A6/A7 manifest chain and final acceptance decision are documented in
[Trading Core integrated candidate and acceptance plan](trading-core-integration-and-acceptance-plan.md).
