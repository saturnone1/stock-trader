# Trading Core Stage 5 final design review

- Review result: Coherent after documented corrections; ready for user acceptance
- Authorization: Design only; implementation, verification execution, deployment, and Remote activation remain prohibited
- Reviewed: ADR 0080–0082, MSA blueprint/roadmap, A0–A7 detailed designs, current Trading Core/Edge/K3s seams
- Date: 2026-09-01

## 1. Executive decision

The Stage 5 design is implementable, preserves the trading invariants, and fits the low-power
single-node K3s target. It is deliberately stronger than an ordinary stateless service extraction
because it moves broker-order authority and open-position protection. The additional machinery is
temporary or build-time where possible:

- one permanent financial service Pod already exists: Trading Core;
- `api-local` and `api-remote` are alternative images for one Edge Pod, not extra replicas;
- the Broker Emulator, acceptance driver/host, and Coordinator are short-lived Jobs/Pods;
- there is no Kafka, service mesh, distributed database, two-phase commit, or permanent orchestrator;
- SQLite remains one-writer per owning service;
- all full verification waits until one A0–A5 candidate is integrated.

The design is therefore proportionate to the failure being prevented: duplicate orders, lost fills,
unprotected positions, or an unsafe rollback.

## 2. Non-negotiable invariants

| Invariant | Design mechanism |
| --- | --- |
| exactly one financial owner | canonical Trading Core epoch ledger plus fenced transition phases |
| exactly one broker-submission capability | image/DI/Secret/egress capability attestation before release |
| no fabricated production evidence | isolated fixtures use their own namespace/PVC/account identities |
| same strategy semantics | shared Engine/TradingCore policy assemblies with byte-hash equality |
| no future-bar or skipped entry-bar behavior | named full conformance corpus in the one local gate |
| durable broker ambiguity | one client order identity and reconciliation rather than blind retry |
| safe open-position handoff | final Local scheduler barrier and per-position evidence watermark replay |
| current nonfinancial Edge data survives rollback | financial-only import into a staging copy of the latest Edge DB |
| monotonic recovery | every completed or aborted attempt consumes one higher generation |
| no hidden fallback | commands remain fenced until explicit release by the effective owner |
| low-power operation | off-server builds, sequential scenarios, bounded ephemeral Pods, heavy-work admission |
| reduced verification churn | one frozen candidate, one full local gate, one linked K3s/production campaign |

## 3. Cross-document findings and resolutions

| Finding | Risk | Final resolution |
| --- | --- | --- |
| `Completed` was followed by `release` | a terminal transition could still mutate | add `ReadyToRelease`; only release enters terminal `Completed` with commands open |
| pre-commit abort had no clean release state | source could remain permanently fenced or reuse a generation | abort consumes the reserved generation, sets `SourceRetained`, enters `ReadyToRelease`, then explicitly releases |
| outbox pending zero was used as a gate | impossible after real events because Reporting is deferred | treat outbox as transactional activity journal; validate integrity/high watermark and only enabled-consumer lag |
| A7 introduced manifest types absent from A0 | final evidence could not validate | add `RemoteRecovery` and `FinalRemote` environment classes |
| Coordinator Pod edges were missing from topology | undocumented NetworkPolicy/RBAC dependency | add temporary Coordinator→Edge/Trading Core/Kubernetes API edges and exact SAN roles |
| dynamic Pod label restriction was attributed to RBAC | Kubernetes RBAC cannot enforce it | allow namespace-wide Pod metadata read only; no Pod logs/exec/delete/patch and application label filtering |
| Edge projection worker mixed accounts and financial snapshots | Remote still carried migration behavior | split account publisher from Local-only financial projection publisher |
| Remote Edge retained broker/provider capability | configuration-only fencing was insufficient | separate `api-remote` image without Local broker/writer assemblies, registrations, workers, or broker Secret references |
| rollback proposed whole-database restoration | post-cutover users/strategies/research could be lost | import only financial compatibility tables into a verified staging copy of the latest Edge DB |
| production broker faults depended on real positions | acceptance could block indefinitely or corrupt state | external durable Broker Emulator plus isolated Trading Core state; genuine Shadow remains a separate gate |
| build and rollout are coupled in the current script | low-power server would rebuild repeatedly | future artifact-import scopes remain inside the one supported `deploy-k3s.sh` entry |

No unresolved contradiction remains among A0–A7 after these corrections.

## 4. Accepted boundary map

### 4.1 Steady-state Pods

```text
Desktop -> Edge api-remote
              |-> Market Data
              |-> ML Training
              |-> Optimization Workers (lease pull in reverse direction)
              +-> Trading Core -> Market Data evidence
                                  -> approved Paper/Live broker adapter
```

Trading Core contains Risk, Order, Position, Fill, Broker reconciliation, and canonical financial
state as modules. They are not future Pod candidates. Strategy Research remains inside Edge unless a
later measured Stage 6 trigger exists. Reporting/Notifications remains absent while no delivery
channel is enabled.

### 4.2 Temporary acceptance/control workloads

- isolated acceptance host, Broker Emulator, and driver exist only in a generated namespace;
- the Cutover Coordinator exists only for an active transition;
- transfer/import Jobs exist only for the exact sealed transition plan;
- their certificates, RBAC, policies, and PVCs are not steady-state dependencies;
- cleanup happens only after manifest copy-out and hash acknowledgement.

## 5. Final authority state model

```text
Requested -> Quiescing -> Draining -> Reconciled -> Committing
                                                   |
                                                   v
                                             Verifying
                                                   |
                                                   v
                                            ReadyToRelease
                                                   |
                                      explicit release only
                                                   v
                                              Completed
```

Before commit, abort republishes the source owner at the reserved higher generation with outcome
`SourceRetained`, then enters `ReadyToRelease`. After commit, the target remains fenced owner; reverse
ownership requires a new transition. `Completed` is the only normal terminal phase and always means
the effective owner's command acceptance is open.

## 6. Final communication and security model

Steady-state communication remains direct private mTLS JSON/HTTPS, durable lease pull, and future
cursor pull. No broker or mesh is added. Temporary ports are limited to:

- acceptance time control `9543` inside the generated namespace;
- Broker Emulator `10443` inside the generated namespace;
- Edge cutover control Service `3543` to Pod port `5543` during an active transition;
- Trading Core control `9443` for the exact Coordinator role;
- Kubernetes API `443` for the Coordinator's scoped Deployment/metadata operations.

Remote Edge may retain unrelated research egress, but it lacks the local financial adapter assembly,
broker SDK/factory registration, broker Secret reference, and writable legacy financial path.
Trading Core alone has the complete broker capability tuple after Remote release.

## 7. Final data-transfer decision

One direction-neutral `CanonicalFinancialTransferV2` carries financial aggregates, immutable
strategy/evidence context, position policy watermarks, idempotency identities, terminal broker/fill
evidence, risk state, activity-journal continuity, schema versions, section hashes, and the overall
transfer hash. It never carries credentials.

Cutover imports it transactionally into Trading Core. Rollback applies it only to the financial
compatibility tables of a staging copy of the current Edge database. `B1 / database snapshot / B2`
evidence must agree before either import can commit. No endpoint or shell script owns a second field
mapping.

## 8. Activity journal decision

The current Trading Core `outbox` is a transactional activity journal while Reporting is deferred.
Undelivered rows do not mean an unresolved broker effect. Stage 5 checks:

- unique event/aggregate-version identities and payload hashes;
- journal/database integrity and transfer high watermark;
- filesystem reserve and bounded growth;
- per-consumer cursor lag only when that consumer is actually enabled.

No journal evidence is deleted during the rollback window. A future archive or Reporting consumer
must receive a separate reviewed policy rather than overloading `delivered_at` globally.

## 9. Manageability and source layout

The implementation should optimize for small context boundaries rather than a smaller line count at
the expense of safety.

### 9.1 Contract files

Do not extend the existing large `TradingCoreContracts.cs` with all v2 records. Split by owner:

```text
src/StockTrader.ServiceContracts/TradingCore/
  CommandContracts.cs
  AuthorityContracts.cs
  TransferContracts.cs
  AcceptanceContracts.cs
  ContractCatalogs.cs
  CanonicalPolicies.cs
```

One catalog owns modes, phases, outcomes, stop reasons, scenarios, environment classes, and versions.
F# hosts consume these contracts; they do not duplicate string lists.

### 9.2 Runtime and adapters

```text
src/StockTrader.TradingCore.BrokerPorts/        provider-neutral broker evidence and risk gate
src/StockTrader.TradingCore.AlpacaAdapter/      production-only provider adapter
workers/trading-core-runtime/                   F# store/workers/reconciliation/scheduler
workers/trading-core-service/                   thin production composition host
workers/trading-core-acceptance/                thin acceptance composition host
workers/trading-core-broker-emulator/           ephemeral external-evidence emulator
workers/trading-core-acceptance-driver/          scenario/manifest Job
workers/trading-core-cutover-coordinator/        resumable transition Job
```

Orchestration files target fewer than 200 nonblank lines; cohesive stores/mappers may be larger only
with named ownership and focused tests. Financial formulas remain in C# Engine/TradingCore policies.
F# is used for concise hosts, workflows, and transport—not for translating formulas.

### 9.3 Edge composition

Use shared Edge endpoint/application assemblies with separate thin Local and Remote composition
hosts. `Dockerfile.api` uses named `local` and `remote` targets rather than duplicated Dockerfiles.
`Dockerfile.trading-core` may likewise use named production/acceptance/emulator/driver/coordinator
targets while copying only each target's allowed assemblies.

Kubernetes templates obtain ports, image identities, role names, resource limits, and generations
from one validated render plan. Shell code orchestrates rendering/import only and does not own
financial state transitions or mapping policy.

## 10. Compressed implementation graph

Implementation starts only after explicit user acceptance of this design. Workstreams share A0
contracts and converge before any full verification or production rollout:

```text
F0 Accept ADR 0082 and freeze v2 catalogs/contracts
 |
 |-- F1 BrokerPorts + Runtime + production/acceptance adapter separation
 |-- F2 Edge Local/Remote hosts + durable fences + account/projection split
 |-- F3 Authority ledger + transfer contracts/mappers + staging import
 |-- F4 Broker Emulator + controlled clock + driver + scenario catalog
 +-- F5 Coordinator + mTLS/RBAC/NetworkPolicy + deploy artifact scopes
                         |
                         v
F6 Integrate all streams, perform focused compile/static checks, close every TODO
                         |
                         v
F7 Freeze one candidate manifest and run the one A6 local full-suite gate
                         |
                         v
F8 Run the one A7 isolated/Shadow/cutover/recovery/rollback/recutover campaign
                         |
                         v
F9 Derive Stage5AcceptanceIndex; accept ADR 0080 and unblock Stage 6 only if Passed
```

F1–F5 do not deploy independently and do not trigger the full repository/K3s suite. A failure found
before F7 receives only focused compiler/static/contract feedback. Any executable change after F7
retires that candidate according to the A6/A7 invalidation matrix.

## 11. Workstream context boundaries

| Stream | Primary context | Must not edit/own |
| --- | --- | --- |
| F1 | broker ports, Runtime composition, provider adapter projects | Edge DB mapping, K3s transition state |
| F2 | Edge composition, command fences, hosted-service inventory | Trading Core canonical store, broker emulator |
| F3 | v2 authority/transfer contracts, ledgers, mappers | provider SDK behavior, UI |
| F4 | isolated host/emulator/driver and acceptance templates | production broker adapter or production DB |
| F5 | Coordinator, role authorization, rendered K3s plan, artifact import | financial formulas or transfer mapping |

Cross-stream changes go through the shared contract/catalog owner. This keeps one task's code and
documentation context bounded for both human and AI maintenance.

## 12. Alternatives rejected

| Alternative | Reason rejected |
| --- | --- |
| wait indefinitely for a genuine open position | couples functional acceptance to account timing |
| insert a production test position/watermark | corrupts the evidence being protected |
| put fault injection in the production Trading Core image | expands a financial attack surface |
| use only mode configuration to prove one writer | leaves adapters, credentials, and stale processes capable |
| dual-write Edge and Trading Core during migration | creates irreconcilable financial ownership |
| restore an old whole Edge DB on rollback | loses unrelated post-cutover application data |
| add Kafka/service mesh/distributed DB | exceeds the single-node resource need and adds failure modes |
| run a permanent transition service | rare operator workflow does not justify another steady Pod |
| verify every implementation fragment in K3s | repeats expensive checks before a complete service candidate exists |

## 13. Intentional limitations

- The one-node cluster is not infrastructure high availability.
- Initial production acceptance is Paper-only and authorizes no Live trading.
- An unresolved external broker effect may keep commands fenced indefinitely; availability never
  overrides correctness.
- Node/storage-loss recovery cannot meet real-money objectives until encrypted off-host backups and
  a restore rehearsal exist.
- Reporting/Notifications and Stage 6 Strategy Research extraction remain separate decisions.
- The design does not promise automatic rollback or zero cutover downtime.

## 14. Final review checklist

- [x] every steady and temporary Pod/network edge is documented;
- [x] every financial state has one owner and one transfer mapping owner;
- [x] source/target authority is unambiguous at every phase and crash point;
- [x] command release is separate from epoch commit and verification;
- [x] isolated evidence cannot masquerade as production evidence;
- [x] production fault injection and synthetic financial state are prohibited;
- [x] Remote Edge capability absence is structural and independently attested;
- [x] rollback preserves current nonfinancial Edge state;
- [x] activity-journal semantics match the absence of a current consumer;
- [x] low-power resource and verification cadence constraints are explicit;
- [x] final authority is an explicitly released higher-generation Remote state;
- [x] no later service extraction begins before Stage 5 derives Accepted.

The design review is complete. The next action requires a user decision: accept this baseline for
implementation or request another design revision. Acceptance of the design still does not itself
authorize Remote activation; that requires the complete A7 evidence campaign.
