# Trading Core integrated candidate and acceptance plan

- Status: Proposed A6/A7 design baseline; no build, test, deployment, or authority change authorized
- Parent designs: [A0 contracts](trading-core-acceptance-contracts.md),
  [A1/A2 isolated acceptance](trading-core-isolated-acceptance-design.md),
  [A3–A5 cutover coordination](trading-core-cutover-coordination-design.md)
- Parent decision: [ADR 0082](adr/0082-define-trading-core-acceptance-and-single-writer-cutover.md)
- Date: 2026-09-01

## 1. Purpose

This document defines how the future A0–A5 implementation becomes one immutable Trading Core Stage
5 candidate and how that candidate is verified once as a service boundary. It prevents the previous
pattern of promoting and fully testing infrastructure fragments one at a time.

A6 is integration and candidate freeze. A7 is one ordered evidence campaign:

```text
one local full-suite gate
  -> one isolated K3s acceptance batch
  -> one production Shadow/cutover/Remote-loss/rollback/recutover batch
  -> one final Stage 5 acceptance record
```

No later MSA boundary starts during this campaign.

## 2. A6 integrated candidate

### 2.1 Candidate contents

One candidate contains every A0–A5 deliverable:

- v2 authority, transition, stop-reason, scenario, transfer, receipt, and manifest contracts;
- shared Trading Core Runtime, broker ports, production broker adapter, and injected clock/factory;
- acceptance host, Broker Emulator, acceptance driver, scenario catalog, and K3s isolation objects;
- durable authority ledger, Coordinator, Edge fences/scheduler barrier, and crash recovery;
- direction-neutral transfer, reconciliation normalizer, Edge staging importer, and Trading Core
  importer/exporter;
- separate `api-local` and `api-remote` composition images and capability attestation;
- exact mTLS roles, NetworkPolicies, resource quotas, storage objects, and the supported deployment
  scopes in the existing deployment entry.

No item is allowed to remain as a TODO, disabled placeholder, unverifiable convention, or manual
database-edit instruction when the candidate is frozen.

### 2.2 Immutable image set

The same source commit and one dependency restore/build invocation produce:

| Image | Runtime use |
| --- | --- |
| `api-local` | current Shadow owner and preserved rollback image |
| `api-remote` | post-cutover Edge with no Local financial/broker capability |
| `trading-core` | production Trading Core candidate |
| `trading-core-acceptance` | isolated host using the same shared Runtime bytes |
| `trading-core-broker-emulator` | ephemeral durable scripted broker |
| `trading-core-acceptance-driver` | ephemeral scenario and manifest Job |
| `trading-core-cutover-coordinator` | short-lived production authority workflow Job |

Only `api-remote` and `trading-core` remain running after final Remote acceptance. Extra images cost
disk space but add no steady-state Pod, CPU, or memory use.

Every image is addressed by immutable digest in evidence and in the transition plan. Mutable tags
are convenience labels only. Rebuilding the same tag does not preserve candidate identity.

### 2.3 Candidate manifest

`TradingCoreCandidateManifestV1` is created before any full verification and contains:

- repository commit, clean/known working-tree input hash, dependency lock/restore hash, build ID;
- image names/digests and base-image digests;
- shared Runtime/Engine/ServiceContracts/BrokerPorts/policy assembly byte hashes;
- production-only and acceptance-only assembly inventories;
- SBOM hashes and package/reference graph hashes;
- database migration set and ordered migration hash for Edge and Trading Core;
- OpenAPI/desktop generated-contract hash;
- Kubernetes object, RBAC, certificate-role, NetworkPolicy, quota, and resource-setting hashes;
- scenario catalog, expected-result catalog, stop-reason catalog, and transfer-mapper hashes;
- exact supported deployment scopes and rollback artifact requirements.

The manifest ID is content-derived. A full gate cannot start until all expected fields exist and
static ownership/reference checks pass.

### 2.4 Candidate freeze rule

After candidate freeze, no source, dependency, migration, container layer, scenario expectation,
manifest, Kubernetes template, or operational option used by the batch may change. Documentation
that does not change an evidence claim may be corrected, but the correction is linked to the same
manifest as non-executable metadata.

If executable input changes, the candidate is retired. Developers may use compiler and focused
checks while preparing a replacement candidate, but the full local/K3s/production sequence is run
only after the replacement is complete and frozen.

## 3. Verification ownership and test layers

### 3.1 Test ownership

| Layer | Owner | Proves |
| --- | --- | --- |
| pure policy | Engine/TradingCore C# tests | trading semantics, reconciliation decisions, hashing, transition legality |
| runtime composition | Trading Core Runtime/host tests | production/acceptance bindings, absence of forbidden references, durable state |
| Edge profiles | application architecture tests | Local/Remote service inventory, write guard, command routing, Secret-independent startup |
| transfer | bidirectional contract tests | exact round trip, section hashes, idempotency, staging safety, compatibility failure |
| service integration | backend integration suite | Edge/Trading Core/Market Data contracts and restart-safe workflow |
| isolated K3s | acceptance driver | broker failures, replay, Pod loss, resource envelope, namespace security |
| production | Coordinator and operator evidence | genuine Shadow compatibility and actual single-writer handoff/rollback |

Tests cannot duplicate trading formulas or implement a second expected simulator. Expected financial
outcomes come from named characterization evidence and stable invariants; the Broker Emulator only
controls external evidence ordering and failure.

The conformance corpus explicitly covers preview/backtest/live use of the same compiled strategy,
future-bar exclusion, next-open entry-bar consumption, conservative ambiguous intrabar ordering,
execution-cost timing, sizing/scaling/partial/full exits, completed-bar cutoffs, calendar/DST,
adjustment identity, evidence correction, and fail-closed unsupported live features.

### 3.2 Required architecture checks

The full gate proves at minimum:

- Domain/Engine and BrokerPorts do not reference HTTP, EF, configuration, provider SDKs, or hosts;
- Runtime references ports but neither production nor scripted broker adapters;
- the production image excludes acceptance host/emulator/driver/control assemblies;
- the acceptance image excludes production broker adapters and broker SDKs;
- `api-remote` excludes Local financial/provider adapter assemblies and forbidden hosted services;
- `api-local` and rollback mapper remain compatible with the current Edge database;
- only named mTLS roles can call evidence, command, fence, or authority operations;
- v1 persisted enum values remain unchanged and v1 authority mutation is fenced after v2 adoption;
- every required stop reason/scenario code is owned by one catalog;
- deployment remains through `scripts/deploy-k3s.sh`; no second root deployment path exists.

## 4. One local full-suite gate

The candidate runs one clean local verification sequence after one restore. The future exact command
orchestration may group commands, but it must include the repository-required checks:

```text
dotnet build StockTrader.csproj --no-restore
dotnet test tests/StockTrader.Tests/StockTrader.Tests.csproj --no-restore
cd desktop-app && npm run api:check
cd desktop-app && npm run test
cd desktop-app && npm run build
```

It also builds every new F#/C# project and runs the dedicated Trading Core contract/runtime,
acceptance-driver, Broker Emulator, transfer, and Edge-profile suites. Existing Market Data service
contract tests run because isolated acceptance uses its evidence boundary. Builds are not repeated
between test projects; tests consume the frozen outputs. The API metadata build performed internally
by the required `api:check` command is part of that one gate, not a separate candidate verification.

The gate then builds all seven OCI images once, generates SBOMs, records digests, extracts assembly
inventories, and proves candidate-manifest consistency. An image rebuilt after this point is another
candidate even if source text appears unchanged.

### 4.1 Local gate result

One `LocalVerificationManifest` records command identities, exit results, test totals, contract and
generated-file hashes, image/SBOM/assembly digests, duration, and sanitized failure codes. It does not
embed console logs, source files, credentials, machine usernames, or absolute secret paths.

Failure stops before K3s. A fix receives focused developer checks while being prepared; once the
replacement candidate is frozen, the complete local gate runs once for that replacement.

## 5. Server admission and resource preparation

Images are built outside the low-power server and imported by the existing supported K3s path. The
server does not compile .NET, npm, or container build contexts during acceptance.

Before the isolated batch:

- keep Market Data ingestion and current Local/Shadow financial protection available;
- pause new ML Training and Optimization work through their existing admission owner, without
  changing their accepted Remote authority;
- verify sufficient disk for imported images, acceptance PVCs, production backups, and off-host
  artifact copy;
- record node CPU, memory, swap, filesystem free space, Pod status, and current service modes;
- reject the run if the node is already swapping, storage is below the declared reserve, or a
  financial/reconciliation health error exists.

The admission receipt becomes part of the acceptance manifest. Paused research work resumes only
after the isolated namespace is removed and the production transition is stable.

## 6. One isolated K3s acceptance batch

### 6.1 Deployment

The future `trading-core-acceptance` scope of `scripts/deploy-k3s.sh` imports the frozen acceptance
images, creates the exact generated namespace and A1/A2 objects, and records their rendered hashes.
It does not roll production Edge or Trading Core, change production authority, or broaden production
broker egress.

### 6.2 Scenario order

The required A0 catalog runs once, one scenario pair at a time:

1. identity/hash conflicts and duplicate command delivery;
2. rejection and timeout-before-submission proof;
3. record-then-timeout and duplicate broker response;
4. delayed/out-of-order partial fills and partial cancellation;
5. contradictory terminal quantity and correction recovery;
6. broker outage and recovery;
7. completed-bar downtime replay and evidence correction fencing;
8. Trading Core Pod loss and Edge-loss autonomous protection;
9. isolated cutover/rollback generation state machine;
10. accepted production-limit resource load.

Ordering starts with cheap fail-fast contract cases and leaves load last, but all results belong to
one sealed manifest and one image set. A failed scenario stops later destructive scenarios by
default. The same scenario may resume only from its durable identity; deleting its state and
restarting as a fresh pass is forbidden.

### 6.3 Isolated exit gate

The batch passes only when the derived `IsolatedAcceptance` manifest is `Passed`, shared assembly
hashes match production images, every required scenario appears exactly once, public broker egress
and production Secret/PVC references are absent, all state converges, and resource objectives pass.

The redacted manifest is copied and hash-acknowledged outside the namespace before exact-run cleanup.
Failure leaves production unchanged and Remote unauthorized.

## 7. Production Shadow evidence gate

The frozen production candidate first runs in `Shadow` with Local Edge remaining the only financial
and broker authority. Trading Core broker egress remains unavailable.

Required evidence includes:

- actual image, contract, TLS, encryption, account, NetworkPolicy, and authority generations;
- at least one genuine market-closed rejection/no-action observation;
- at least one genuine market-open entry or position observation through normal Edge behavior;
- exact entry/position decision and mutable policy-state parity with no unexplained mismatch;
- Market Data evidence-role success and provider/mutation-route denial;
- current open-position artifact/watermark compatibility or a recorded zero-position state;
- zero unresolved broker effect and quantity divergence, an integrity-valid activity journal,
  acceptable lag for every enabled consumer, and no health error;
- steady resource samples while normal research workloads are admitted.

If natural Paper activity does not produce the market-open observation, a separately approved
Paper-only command may pass through the normal Edge boundary. It cannot insert rows, invent a
position/watermark, bypass risk, or contact a Live endpoint.

The Shadow gate may span open and closed market windows without rebuilding or altering the
candidate. Waiting is not repeated verification. Any executable candidate change invalidates the
gate and returns to A6.

## 8. Production cutover rehearsal

The accepted Coordinator executes the A3–A5 P0–P10 state machine against the frozen plan:

1. confirm isolated and Shadow manifest IDs plus checked backups;
2. reserve the next generation and fence both command paths;
3. finish the Local position cycle, enter its scheduler barrier, and drain reconciliation;
4. capture and seal `B1/D/B2` Edge-to-Trading Core transfer evidence;
5. import and verify candidate canonical state;
6. commit the Remote generation while both sides remain fenced;
7. deploy `api-remote` and prove Local adapter/writer/Secret absence;
8. grant Trading Core the exact Remote broker capability and restricted egress;
9. reconcile and run one autonomous completed-bar protection cycle;
10. attest one writer, explicitly release Trading Core commands, and seal the cutover manifest.

The first production rehearsal is Alpaca Paper only. Live endpoints and real-money account
generations are rejected by the transition plan.

## 9. Remote recovery evidence

While Remote is the sole owner, one bounded recovery set runs before rollback:

- delete the exact Trading Core Pod and prove PVC/epoch/intent continuity;
- require readiness and reconciliation to resume within the documented objective;
- if there is an in-flight Paper command, preserve its client order identity and converge broker
  evidence without resubmission; otherwise use a separately approved harmless Paper scenario through
  the normal command boundary;
- temporarily make Edge unavailable and prove open-position protection remains owned by Trading
  Core while no user intent is invented;
- prove `api-remote` restart retains its authority mirror and cannot perform a Local financial write.

No node-loss, Live trading, destructive production evidence correction, or unbounded load is part of
this single-node rehearsal. Those claims are neither implied nor recorded as passed.

## 10. Reconciled production rollback rehearsal

The Coordinator executes R0–R8 from A3–A5:

1. reserve another generation and fence Trading Core commands;
2. reach the safe protection barrier and reconcile every broker effect;
3. seal the Trading Core-to-Edge transfer;
4. import only financial compatibility state into a staging copy of the latest Edge DB;
5. verify and atomically replace the stopped Edge database;
6. remove Trading Core broker capability while it remains fenced owner;
7. commit the higher Edge-owned Shadow generation;
8. deploy the preserved `api-local`, attest one Edge writer, reconcile once, and release Local;
9. seal `ProductionRollback` evidence.

The application database's nonfinancial row/hash inventory before and after staging import must
match. Failure leaves Trading Core as the fenced owner and does not start Local.

## 11. Final Remote recutover

Stage 5 targets Trading Core as the production financial authority, so a successful rollback
rehearsal is followed by an explicit recutover using the same frozen image set and another higher
generation. The second cutover may reuse already accepted code and scenario evidence but must create
fresh source snapshot, broker reconciliation, backup, capability, and authority receipts.

Final release requires:

- `api-remote` and Trading Core actual digests match the candidate;
- Trading Core is `Remote`, command acceptance is open, and Edge is the read/command gateway only;
- one complete post-release reconciliation/protection cycle passes;
- no Local financial writer, broker adapter, broker Secret reference, unresolved effect, divergence,
  activity-journal integrity failure, excessive enabled-consumer lag, health error, unexpected
  restart, OOM, or swap pressure exists;
- Desktop financial reads and permitted Paper command status use Trading Core canonical projections;
- preserved rollback artifacts are named, hash-checked, and copied according to the rollback window.

The operator explicitly releases the final Remote generation. A successful rehearsal does not open
commands automatically.

## 12. Evidence set and acceptance decision

Stage 5 produces one linked set:

| Artifact | Purpose |
| --- | --- |
| Candidate manifest | immutable source/image/contract/K8s identity |
| Local verification manifest | build, tests, contracts, image/SBOM consistency |
| IsolatedAcceptance manifest | deterministic failure/replay/load/security evidence |
| ProductionShadow manifest | genuine deployed parity evidence |
| ProductionCutover manifest | first one-writer handoff |
| RemoteRecovery manifest | owner-Pod/Edge loss and reconciliation continuity |
| ProductionRollback manifest | reconciled reverse transfer and Edge-only authority |
| FinalRemote manifest | higher-generation recutover and final steady state |

An `Stage5AcceptanceIndex` contains only their IDs/hashes, candidate ID, chronological authority
generations, final authority, unresolved counts, active stop reasons, review identity, and UTC time.
It derives `Accepted`; an operator cannot set the value independently.

ADR 0080 may become Accepted and Stage 5 may be marked complete only when the index derives
`Accepted`. Stage 6 remains blocked otherwise.

## 13. Failure and rerun policy

### 13.1 Evidence invalidation

| Change after failure | Required rerun |
| --- | --- |
| financial policy, Runtime, broker adapter, scheduler, transfer mapper, migration, or contract | retire candidate; restart from local full gate |
| acceptance driver/emulator/scenario semantics | retire candidate; local gate and isolated batch again |
| Edge profile/composition or capability attestation | retire candidate; local gate, isolated affected checks, Shadow and production sequence again |
| Kubernetes security/resource template | retire candidate; static gate and all K3s evidence affected by it |
| expired certificate or preserved Secret generation with unchanged binary | repeat identity/rotation and later dependent K3s phases; do not rerun pure tests |
| transient observation window with unchanged candidate/state | resume same phase/identity after stop reason clears |
| documentation wording that changes no executable/evidence claim | no rerun; link correction to manifests |

### 13.2 Stop behavior

Before epoch commit, abort consumes the reserved generation and retains the source owner. After epoch
commit, the target remains fenced owner and recovery either completes that transition or creates a
new reverse transition. The system never automatically selects Local, restores an old database, or
reuses partial evidence from a different candidate.

## 14. Low-power server timing policy

- Build and package outside the server; import immutable images once.
- Run one isolated scenario pair at a time.
- Pause heavy ML/optimization work during isolated load and authority transitions.
- Keep Market Data and the active financial owner prioritized and continuously observable.
- Do not perform cutover or rollback while the broker reconciliation deadline cannot fit inside the
  approved operator window.
- Stop on sustained swap, filesystem reserve breach, OOM, missed reconciliation, or repeated health
  timeout; raising resource limits mid-run creates another candidate.

## 15. Deployment entry and scopes

`scripts/deploy-k3s.sh` remains the only supported K3s entry. Future explicit scopes may cover:

```text
trading-core-acceptance
trading-core-shadow-candidate
trading-core-cutover
trading-core-rollback
trading-core-recutover
```

Scopes render only the frozen plan and invoke the relevant Job/state operation. They do not contain
an independent authority state machine, mapping rules, expected financial values, or hidden fallback.
Docker Compose remains local development only and is not an acceptance substitute.

## 16. A6/A7 design exit criteria

A6/A7 are ready for review when:

- one manifest identifies every executable, image, contract, migration, scenario, and K3s input;
- no partial A0–A5 package can be promoted independently;
- the required repository verification commands and new service suites run once per frozen candidate;
- isolated, Shadow, cutover, recovery, rollback, and final Remote evidence have explicit order and
  non-substitution rules;
- the server never builds images and heavy research work does not contend with financial acceptance;
- failure invalidation is proportional but never combines evidence from different binaries;
- production rollback preserves current nonfinancial Edge state and proves one higher generation;
- final production state is explicit Remote, not an accidental consequence of the rehearsal;
- Stage 5 acceptance and Stage 6 release are mechanically derived from the complete evidence index.

Until this design is accepted and A0–A5 implementation is integrated, no full gate, acceptance
namespace, production cutover, rollback, recutover, or Remote activation is authorized.
