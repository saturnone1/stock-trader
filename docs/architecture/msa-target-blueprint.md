# StockTrader MSA target blueprint

This document is the structured design baseline for the remaining MSA work. ADR 0081 owns the
communication decision; this blueprint maps that decision to the current code, data, Kubernetes
workloads, end-to-end flows, failure behavior, and migration batches. An implementation item not
traceable to this blueprint requires an ADR update before code changes.

## 1. Scope and state notation

- **Current** means deployed and authoritative in production.
- **Candidate** means deployed but non-authoritative or shadowed.
- **Target** means required before the owning Pod is complete.
- **Conditional** means prohibited until a measured extraction trigger is met.

The design optimizes for a low-power single-node K3s server. It provides workload isolation and
clear ownership, not physical high availability.

## 2. Architecture invariants

1. Preview, backtest, optimization, and live execution consume the same strategy document,
   compiler version, catalogs, and deterministic engine semantics.
2. Exactly one component owns each durable fact and exactly one financial authority may create an
   external effect.
3. No Pod reads or writes another Pod's database or hostPath.
4. Every cross-Pod message is versioned, authenticated, bounded, observable, and idempotent where
   an effect is possible.
5. Missing, stale, corrected, ambiguous, or incompatible evidence fails closed.
6. A network timeout after a possible broker effect is reconciled; it is never treated as a clean
   retry opportunity.
7. Operational configuration is typed and validated. Catalog facts and mathematical invariants keep
   one code owner. Secrets are never configuration defaults.
8. Risk, orders, broker evidence, reconciliation, positions, fills, and trades remain one Trading
   Core boundary.

## 3. System context

```text
User
  |
  v
Desktop ---- public HTTPS ----> Edge / Control API
                                  |
                                  +---- private mTLS ----> Market Data ----> data providers
                                  |
                                  +<--- durable lease --- Optimization Workers
                                  |
                                  +---- private mTLS ----> ML Training
                                  |
                                  +---- private mTLS ----> Trading Core ----> broker APIs
                                                               |
                                                               +---- evidence-only mTLS ----> Market Data
```

Only Desktop and Edge are user-facing. Internal service ports are not exposed by Ingress. Strategy
Research is currently an Edge module, not a Pod. Reporting/Notifications is absent, not an implied
service.

## 4. Workload catalog

| Workload | Current state | Target responsibility | Scale model | Completion state |
| --- | --- | --- | --- | --- |
| Desktop | Current Deployment | UI only; server-owned metadata and JSON API | 1 | complete |
| Edge / Control API | Current Deployment and application DB owner | auth, settings, aggregation, strategy research until triggered | 1 while SQLite | retained boundary |
| Optimization Worker | Current F# Deployment, two Pods | stateless deterministic evaluation from immutable leases | horizontal, bounded | complete |
| Market Data | Current F# Deployment, Remote authority | providers, normalized bars, corrections, evidence | 1 while SQLite | complete |
| ML Training | Current F# Deployment, Remote authority | durable training queue and immutable publications | 1 on current server | complete |
| Trading Core | Candidate F# Deployment, Shadow generation 2 | sole Remote financial authority | 1; fenced single-active | active completion batch |
| Strategy Research | Current module inside Edge | authoring, preview, backtest, scan, signal and optimization ownership | conditional Pod | do not extract yet |
| Reporting/Notifications | Disabled module behavior | cursor-based non-authoritative projection/delivery | conditional Pod | do not extract yet |

No additional Pod is created for risk, order, position, broker, scheduler, catalog, or database
migration concerns.

The final default backend therefore has five bounded workload types: Edge, Optimization Worker,
Market Data, ML Training, and Trading Core. Desktop is a separate UI workload. Strategy Research and
Reporting are conditional future boundaries, not unfinished mandatory Pods.

## 5. Code and dependency view

```text
Kubernetes hosts / HTTP adapters
        |
        v
Application use cases -----> purpose-specific ports
        |                              ^
        v                              |
Engine / Domain <-------------- Infrastructure adapters

Service hosts ------> StockTrader.ServiceContracts
Service hosts ------> only the deterministic compute/policy assembly they execute
```

- `StockTrader.Engine` owns deterministic indicators, rules, fills, costs, and portfolio math.
- `StockTrader.ServiceContracts` owns transport DTOs and compatibility validation, not business
  policy or persistence.
- `StockTrader.TradingCore` owns reusable financial command policy shared with the candidate host.
- F# service projects own concise hosting and orchestration. They do not copy catalogs or trading
  formulas.
- The root ASP.NET project remains the composition root for Edge and compatibility adapters; it is
  not referenced by an extracted service.

## 6. Data ownership

### 6.1 Canonical stores

| Durable fact | Current canonical owner | Target canonical owner | Compatibility rule |
| --- | --- | --- | --- |
| users, login audit, settings | Edge application DB | Edge | never copied to service databases |
| strategy documents, symbol profiles, universe definitions | Edge/Strategy Research | Edge/Strategy Research | Trading Core receives immutable artifact snapshots only |
| optimization jobs, leases, accepted results | Edge/Strategy Research | Edge/Strategy Research | workers hold no durable canonical state |
| normalized OHLCV, series revision, correction evidence | Market Data DB | Market Data | Edge cache/read model is non-authoritative |
| financial snapshots/import runs used for research | Edge/Strategy Research | Edge/Strategy Research | not broker account state |
| ML training jobs, model publications, artifact hashes | ML Training DB/artifact volume | ML Training | Edge retains only verified inference cache |
| executable account configuration generation | Local Edge while Shadow | Trading Core in Remote | control metadata may remain Edge; secret material is never projected back |
| recommendations accepted for execution | Local Edge while Shadow | Trading Core in Remote | Edge read model becomes projection only |
| orders, broker evidence, reconciliation state | Local Edge while Shadow | Trading Core | never dual-written after cutover |
| positions, scaling state, fills, realized trades, risk state | Local Edge while Shadow | Trading Core | legacy tables become rollback snapshot/read projection |
| notification/report delivery state | none/Edge local behavior | conditional consumer store | cannot feed trading decisions |

### 6.2 Shared-database prohibition

The application, Market Data, ML Training, and Trading Core SQLite files have different host paths
and owners. A migration crosses a service boundary through versioned import/projection contracts,
not SQL, file copies, attached databases, or cross-database foreign keys. Backup and restore operate
per owner. A multi-store financial repair is a stop condition.

### 6.3 Hosted-worker disposition

The current Edge process still registers transitional hosted services. Their final owner or removal
is fixed before Trading Core cutover:

| Current hosted service | Target owner | Target disposition |
| --- | --- | --- |
| `AlpacaStreamingService` | Market Data | remove the Edge provider loop in Remote |
| `MarketDataIngestionService`, `DailyDataSyncService` | Market Data | move scheduling beside provider/store ownership; Edge may request an operator run only |
| `MarketDataSubscriptionSyncService` | Edge -> Market Data | retain user-selected-symbol control synchronization |
| `MarketDataShadowBackfillService` | migration-only | remove after the compatibility rollback window |
| `PatternScannerService` | Strategy Research module | remain in Edge until optional Research extraction |
| `RiskMonitorService` | Trading Core | disabled in Edge Remote |
| `EntryExecutionReconciliationService` | Trading Core | disabled in Edge Remote |
| `PositionExecutionManagerService` | Trading Core | disable its Edge scheduler in Remote |
| `TradingCoreProjectionService` | migration-only | remove after Remote cutover and rollback window |
| `TradingCorePositionShadowService` | migration-only | remove after Shadow acceptance |
| `MLRetrainingService` | Edge/Research -> ML Training | retain scheduling policy; ML Training owns execution/publication |
| `MlTrainingPublicationReconciliationService` | Edge inference cache | retain verified-cache reconciliation only |
| `ContinuousOptimizationService` | Strategy Research module | remain in Edge; compute leases stay remote |
| `FinancialSnapshotIngestionService` | Strategy Research module | remain in Edge unless Research is extracted |
| `DailyReportService` | Edge until a delivery trigger | no new Pod yet |

Remote registration is explicit rather than relying on a hosted service to wake up and no-op. No
Edge financial scheduler may remain active after Trading Core becomes Remote authority.

## 7. Communication contract registry

| Edge | Pattern | Current contract owner | Required identity |
| --- | --- | --- | --- |
| Desktop -> Edge | public request/response | OpenAPI desktop document | user/session, request correlation |
| Worker -> Edge | lease pull/heartbeat/result | `OptimizationContracts.cs` and prepared-data contracts | job, lease generation, input hash, result hash |
| Edge -> Market Data | private command/query | `MarketDataContracts.cs` | request/evidence ID, revision, content hash |
| Edge -> ML Training | private command/query | `MlTrainingContracts.cs` | job ID, input hash, trainer/schema versions, publication hash |
| Edge/Research -> Trading Core | private command/query | `TradingCoreContracts.cs` | command, correlation/causation, authority/account generation, artifact/evidence hashes |
| Trading Core -> Market Data | dedicated execution-evidence query | Market Data execution subset | verify named evidence and fetch bounded latest completed bars; no provider/upsert authority |
| Trading Core -> broker | adapter-specific protocol behind one port | Trading Core broker adapter | durable client order ID and account generation |
| Consumer -> Trading Core | conditional cursor pull | future activity-event contract | event ID, monotonic cursor, consumer inbox ID |

Contracts serialize enums by explicit agreed names and financial decimals by invariant JSON numeric
representation with domain range validation. Timestamps are UTC instants; exchange-local dates and
calendar versions remain separate explicit fields. Internal endpoints are excluded from public
OpenAPI.

The Trading Core evidence identity is authorized only for dedicated read-only operations:
`POST /v1/execution-evidence/verify` validates a named revision/hash, and
`POST /v1/execution-evidence/latest-completed` returns the bounded completed-bar window required by
one stored position artifact. The latter reads canonical persisted bars only and never triggers a
provider call. It cannot use `/v1/bars/upsert`, `/v1/provider/*`, subscriptions, broad research
ranges, or correction administration. NetworkPolicy limits the Pod and port; application
authorization limits the operation. Both controls are required.

Breaking contract evolution uses a parallel endpoint or compatibility reader. A deployment never
relies on two independently upgraded Pods interpreting the same version differently.

### 7.1 Workload roles and operation authorization

| Certificate role | Allowed operations | Explicitly denied |
| --- | --- | --- |
| `optimization-worker` | Edge lease claim, heartbeat, result, worker status | every public API and all other services |
| `edge-market-data` | bounded bars/provider operations, subscriptions, corrections required by research/control | service administration outside the contract |
| `edge-ml-training` | submit/cancel/query jobs and query publications | artifact file mutation and other services |
| `edge-trading-control` | projection, account generation, authority, commands, Shadow, canonical queries | direct DB and broker access |
| `trading-core-evidence` | verify named evidence and fetch bounded latest completed bars for execution | research ranges, provider calls, upsert, subscriptions, corrections |
| future `strategy-research` | Trading Core recommendation/entry commands and command query | authority, account configuration, broker, canonical mutation APIs |
| future `reporting-reader` | Trading Core activity cursor read | commands, account, authority, broker |

The role is encoded in a certificate SAN and matched exactly after private-CA and client-auth-EKU
validation. A caller is authorized by role and operation, never merely because any certificate from
the CA was presented. Existing shared-secret headers remain accepted only during a staged migration:
servers first accept valid role certificates, clients stop sending the header, usage telemetry proves
the legacy path is unused, and then header handling and Secrets are removed.

### 7.2 Contract evolution and retry classes

Additive optional fields may remain in a major contract version when old readers preserve the same
meaning. A required-field, enum-meaning, decimal-unit, identity, or execution-semantic change uses a
parallel `/v2` operation and contract type. Consumers deploy first; the previous reader remains for
one verified rollback release and is then removed deliberately.

| Operation class | Examples | Retry rule |
| --- | --- | --- |
| pure bounded query | status, portfolio, command status, evidence verify | bounded retry within deadline; no semantic fallback |
| idempotent state publication | projection snapshot, ML job input, subscription generation | same identity and payload hash only |
| durable internal command | entry, position, account configuration | same command/generation and payload hash only; conflict on changed payload |
| authority transition | Local/Shadow/Remote generation | operator-coordinated; query status before any repeat |
| broker external effect | submit/cancel | never retry ambiguous effect as a new identity; reconcile by durable client ID |

### 7.3 Target internal endpoint surface

| Service | Operation family | Authorized role | Semantics |
| --- | --- | --- | --- |
| Edge `3443` | `/internal/optimization-worker/leases/*` | `optimization-worker` | claim/heartbeat/result under lease generation |
| Market Data `7443` | `/v1/bars/*`, `/v1/provider/*`, subscriptions/corrections | `edge-market-data` | research/control data operations |
| Market Data `7443` | `/v1/execution-evidence/verify`, `/latest-completed` | `trading-core-evidence` | exact verification or bounded persisted completed bars; never provider access |
| ML Training `8443` | `/v1/training/jobs/*`, `/v1/publications/*` | `edge-ml-training` | durable job and immutable publication operations |
| Trading Core `9443` | `/v1/control/projections`, account configuration, authority | `edge-trading-control` | control plane; authority is monotonic and operator-coordinated |
| Trading Core `9443` | `/v1/commands/*`, `/v1/recommendations` | `edge-trading-control`; future restricted Research role | idempotent durable financial intent acceptance |
| Trading Core `9443` | `/v1/portfolio`, command status, activity cursor | authorized read role | canonical queries only |

All effect-bearing messages use a common envelope shape: contract version, stable message/command
ID, idempotency key, correlation ID, optional causation ID, producer role, occurred/observed UTC,
payload hash, and optional deadline/authority/account generation. Contract validators reject unknown
required enum values, unit ambiguity, non-canonical decimals, non-UTC instants, and a hash that does
not match the canonical serialized payload.

## 8. End-to-end sequences

### 8.1 Preview and backtest

```text
Desktop -> Edge: StrategyDocument + symbols + timeframe/calendar/adjustment/range
Edge -> Market Data: bounded evidence request
Market Data -> Edge: bars + evidence identity/revision/hash/completeness
Edge -> Engine: compile once and execute deterministic preview/backtest
Edge -> Desktop: result + exact evidence and execution assumptions
```

Trading Core and broker adapters are not involved. A missing or incomplete range is visible; Edge
does not silently change provider, timeframe, adjustment, or dates.

### 8.2 Optimization

```text
Desktop -> Edge: create optimization job
Edge: persist immutable input and lease generation
Worker -> Edge: claim
Edge -> Worker: compiled strategy input + prepared bars/evidence + hashes
Worker -> Edge: heartbeat, then result under same lease/input hash
Edge: accept once if lease/cancellation/input still match
Desktop -> Edge: query accepted results
```

Workers never call Market Data, ML Training, Trading Core, or a broker. This keeps retries cheap and
prevents provider variation between candidates.

### 8.3 ML training and publication

```text
Edge -> Market Data: obtain exact training evidence
Edge -> ML Training: immutable samples + feature/trainer schema + input hash
ML Training: persist job, train within resource limit, publish content-addressed artifact
Edge -> ML Training: query latest publication
Edge: verify schema/hash and atomically update inference cache
```

Training outage leaves the last verified model active. It cannot mutate a live strategy or trigger
an order.

### 8.4 Automatic or manual live command

```text
Edge/Research -> Market Data: completed-bar evidence
Edge/Research: compile immutable StrategyExecutionArtifact
Edge/Research -> Trading Core: stable command + artifact + evidence + expiry + generations
Trading Core -> Market Data: verify evidence ID/revision/hash
Trading Core: validate authority, account, feature parity, risk, duplicate/conflict, expiry
Trading Core: durably accept intent
Trading Core -> Broker: submit once with durable client order ID
Trading Core -> Broker: reconcile order/fill evidence until terminal
Trading Core: atomically update order/position/fill/trade/risk and activity outbox
Edge -> Trading Core: query canonical projection for Desktop
```

Manual execution enters at the same command boundary and cannot bypass completed-bar evidence,
strategy compatibility, risk, or reconciliation.

### 8.5 Open-position protection

Once an entry fill creates a position, Trading Core owns its continuing protection independently of
Edge availability:

```text
Trading Core scheduler: position is due at its explicit timeframe/calendar boundary
Trading Core -> Market Data: latest completed-bar evidence for the stored execution artifact
Trading Core: verify evidence, evaluate shared deterministic position policy
Trading Core: persist highest/stop/risk/breakeven/trailing state even when no order is due
Trading Core: if an exit/scale action is due, create one durable command identity
Trading Core -> Broker: submit/reconcile under that identity
Edge -> Trading Core: read resulting canonical position/activity projection
```

The Edge `PositionExecutionManagerService`, risk monitor, and entry reconciliation loops are removed
from Remote registration only after equivalent Trading Core cycles are active. This prevents an Edge
outage from leaving an already open position unmanaged.

### 8.6 Authority cutover

```text
Edge: stop creating new Local intents and drain/reconcile every outstanding order
Edge -> Trading Core: final snapshot/import watermark
Operator: verify stores and broker have no unresolved divergence
Edge -> Trading Core: higher monotonic Remote authority generation
Deploy configuration: Trading Core Remote first, Edge Remote consumers second
Trading Core: release only commands carrying the active generation
```

Rollback disables new Remote acceptance, reconciles every broker effect, records a higher generation,
projects the canonical snapshot back, and only then enables Local. Changing an environment variable
alone is never rollback.

## 9. Failure model

| Failure | Expected owner behavior | User-visible behavior | Forbidden fallback |
| --- | --- | --- | --- |
| Market Data unavailable | no new evidence; existing identified evidence remains immutable | preview/live evaluation reports unavailable or stale | inventing bars or silently switching semantics |
| Worker lost | lease expires and another worker reclaims higher generation | job delayed | accepting stale result twice |
| ML Training lost | durable job resumes; last verified model remains | training delayed | loading partial artifact |
| Trading Core lost in Shadow | Local stays authoritative; comparison unavailable | health degradation only | promoting candidate automatically |
| Trading Core lost in Remote | block new commands; recover durable intents and reconcile broker | trading degraded/unavailable | automatic Local order writer |
| Edge lost | internal stores/services retain state; no new user commands | UI unavailable | services inventing user intent |
| one-sided certificate rotation | dependency isolated and named in health | Local remains available in Shadow | changing authority |
| broker timeout before proven submission | follow adapter-specific lookup rules | pending/reconciling | new client order ID |
| broker timeout after possible submission | retain intent and query evidence | pending/reconciling | blind submit retry |
| corrected evidence | fence dependent command/model until acknowledged | explicit rejection | using stale cached result |
| contract/version mismatch | reject before durable effect | explicit incompatibility | best-effort deserialization |
| database corruption | fail startup before schema mutation | owning service unavailable | creating an empty replacement DB |

## 10. Security and NetworkPolicy model

The allow-list is directional:

| Source | Destination | Port | Mode |
| --- | --- | ---: | --- |
| Ingress/Desktop | Edge | 3000 | current |
| Optimization Worker | Edge internal | 3443 | current |
| Edge | Market Data | 7443 | current |
| Edge | ML Training | 8443 | current |
| Edge | Trading Core | 9443 | current Candidate |
| Trading Core | Market Data evidence endpoint | 7443 | Target before Remote |
| Trading Core | broker HTTPS, application-host allow-list | 443 | Remote only |

Every service also gets DNS egress and cluster-local health/metrics access only as documented.
Trading Core-to-Market Data uses a dedicated client certificate and caller role, not Edge
credentials. Market Data maps that identity only to evidence verification. NetworkPolicy widening
and Remote authority are separate reviewed
changes; Projection and Shadow never receive broker egress.

### 10.1 Account-encryption key migration protocol

The account row records the encryption-key generation that produced its ciphertext. Encryption
Secrets use preserved names such as `stocktrader-trading-core-encryption-<generation>`; `legacy`
names the existing unversioned Secret during migration only.

Rotation is performed before Remote cutover and never by merely changing a Deployment reference:

1. quiesce API account changes and stop Trading Core after confirming it is non-authoritative;
2. create a checked database backup and preserve the old key generation;
3. run an offline migration from the Trading Core image with only old/new keys and the candidate DB
   mounted;
4. inside one SQLite transaction, decrypt with the row's recorded generation, validate the existing
   configuration hash, encrypt with a fresh nonce under the new generation, verify a round trip in
   memory, update ciphertext/tag/nonce/generation, and append an audit record;
5. zero plaintext buffers, commit, run database integrity and decryptability checks, then start
   Trading Core with only the new active key;
6. start API after Trading Core readiness and verify the unchanged account generation/configuration
   hash.

No plaintext or key is written to logs, arguments, backup metadata, or disk. Failure before commit
leaves the old row/key valid. Failure after commit rolls back by restoring the paired pre-rotation
database backup and old Secret while both Pods are stopped. Remote rotation requires a separate
broker-reconciled procedure and is not part of the first cutover.

### 10.2 External egress enforcement

Default K3s NetworkPolicy can restrict external traffic by CIDR and port but cannot safely pin a
cloud provider's changing DNS name. Remote Trading Core therefore uses two controls: egress permits
public TCP 443 while excluding private/cluster ranges, and the broker adapter accepts only validated
HTTPS base URIs from the central broker capability catalog. Trading Core exposes no generic HTTP
proxy/client endpoint, and broker credentials are scoped to that Pod. If a network-layer FQDN
allow-list becomes mandatory, a measured egress gateway requires a separate ADR; basic
NetworkPolicy must not be described as exact FQDN enforcement.

## 11. Observability model

Logs, metrics, and traces use the same correlation, causation, job, command, evidence, authority
generation, account generation, strategy artifact, and broker-order identities carried by contracts.
Secrets, credential ciphertext, cookies, authorization headers, and full account identifiers are
redacted.

Minimum service metrics are request latency/error by contract operation, queue/lease age, active
work, durable inbox/outbox backlog, evidence revision/freshness, unresolved financial intents,
broker/canonical divergence, last successful reconciliation, memory, CPU, and restart count.

Health separates:

- liveness: process can make progress;
- readiness: service can safely perform its owned operation;
- dependency degradation: named downstream failure without inventing fallback;
- authority: mode, monotonic generation, and unresolved-effect count for Trading Core.

### 11.1 Low-power server resource policy

The current manifest budgets are the starting ceiling, not evidence that every limit may be consumed
at once:

| Workload | Replicas | CPU request / limit each | Memory request / limit each | Runtime class |
| --- | ---: | ---: | ---: | --- |
| Desktop | 1 | 100m / 200m | 128Mi / 256Mi | steady |
| Edge | 1 | 200m / 500m | 256Mi / 512Mi | steady |
| Market Data | 1 | 100m / 1 core | 128Mi / 512Mi | steady with provider bursts |
| Trading Core | 1 | 50m / 500m | 96Mi / 256Mi | steady, priority financial |
| ML Training | 1 | 100m / 2 cores | 256Mi / 1Gi | heavy batch |
| Optimization Worker | 2 | 100m / 2 cores | 128Mi / 1Gi | heavy batch |

Edge owns heavy-compute admission because it creates both ML and optimization work. On the current
single node, ML Training and a full multi-worker optimization batch do not run concurrently. Trading
Core, Market Data ingestion, health, and reconciliation are never CPU-starved by research work.
Concurrency and pause thresholds are typed operational options. Kubernetes requests/limits remain a
last guard, not the scheduler policy.

The completion sample records steady and peak CPU, memory, queue age, request latency, SQLite busy
time, and swap pressure. Any sustained swapping, OOM restart, missed reconciliation interval, or
health timeout under the acceptance load rejects the resource configuration.

### 11.2 Initial service objectives

These are acceptance targets for the current single node, not an HA claim:

| Concern | Target |
| --- | --- |
| acknowledged internal command durability | process/Pod-loss RPO 0 on the persistent volume |
| Trading Core Pod recreation | Ready and reconciliation active within 2 minutes |
| non-Remote checked restore | service restored within 30 minutes |
| Remote recovery | new commands fenced immediately; reconciliation begins within 2 minutes; operator recovery target 60 minutes |
| storage-device disaster | at most 15 minutes of DB backup exposure plus mandatory broker reconciliation; external backup location required before real-money use |
| completed-bar evidence | exact revision/hash and explicit freshness deadline; no stale fallback |
| live command acceptance | durable local acceptance within 1 second under accepted load, excluding broker latency |
| internal bounded query | p95 below 500 ms under accepted load |

Hardware loss cannot meet these objectives while backups live only on the same server. Real-money
authority is prohibited until encrypted off-host Trading Core backups and a restore rehearsal exist.

## 12. Deployment and compatibility waves

1. Add backward-compatible consumer support and contract validation.
2. Deploy the producer without changing authority.
3. Exercise Projection/Shadow with the old owner still authoritative.
4. Complete the entire Pod's recovery, rotation, load, and failure behavior.
5. Run one full local verification suite for the completed Pod boundary.
6. Run one production acceptance batch and record evidence.
7. Cut over one authority with a monotonic generation.
8. Preserve the previous image, contract reader, secret generations, and reconciled store until the
   rollback window closes.

Small internal edits may use compiler or focused checks, but they do not trigger the full backend,
desktop, image, and K3s verification sequence. That sequence belongs to steps 5 and 6.

## 13. Trading Core completion batch

No later extraction starts until this complete batch is delivered:

- migrate every internal edge to exact certificate-role authorization and retire shared transport
  secrets after compatibility telemetry reaches zero;
- implement transactional account-credential re-encryption with old/new key generations and a
  verified rollback artifact;
- add the dedicated Trading Core identity and read-only Market Data evidence verification contract;
- run controlled duplicate, ambiguity, timeout, delayed/out-of-order, cancellation, partial-fill,
  broker-outage, and Pod-loss scenarios against deterministic broker evidence;
- prove resource bounds under realistic position/command/reconciliation load;
- collect real Shadow decisions in both market-closed and market-open conditions;
- perform the single-writer Remote cutover and fully reconciled Remote-to-Local rollback rehearsal;
- only after all implementation items are present, run the required full test/build and K3s
  acceptance suite once.

Trading Core is complete only when API/desktop reads remain correct with legacy financial writes
disabled, exactly one broker authority is provable, all in-flight effects converge after restart,
and rollback requires no manual multi-database editing.

### 13.1 Work-package dependency graph

The completion batch is coordinated from frozen contracts instead of being deployed as a chain of
small fixes:

```text
T0 Contract and ownership freeze
 |-- T1 Account encryption migration + rollback artifact
 |-- T2 Dedicated Market Data execution-evidence identity/endpoints/client
 |-- T3 Deterministic broker-failure harness + convergence cases
 |-- T4 Remote hosted-service fencing + observability
 +-- T5 Certificate-role authorization + shared-secret retirement
                 |
                 v
T6 Integrate all packages into one Trading Core candidate image
                 |
                 v
T7 One local full-suite gate -> one K3s Shadow/Remote/rollback acceptance batch
```

T1–T5 share the T0 contracts. None is separately promoted to production or counted as an MSA
milestone. T6 is the first deployable completion candidate.

### 13.2 Current gaps found by design-to-code review

| Gap | Current behavior | Required resolution |
| --- | --- | --- |
| Market Data caller authorization | one common name/shared secret protects every `/v1` operation | exact SAN roles and dedicated Trading Core execution-evidence operations |
| Trading Core caller authorization | CA trust plus shared secret; no explicit client role check | bind exact Edge role to control/command operations, then retire the header secret |
| account encryption rotation | one unversioned key decrypts persisted credentials | transactional generation migration, verification, and rollback artifact |
| Edge financial schedulers | the position manager is registered outside the existing Remote conditional block | explicitly fence every financial hosted service in Remote |
| broker failure acceptance | convergence code exists but controlled operational evidence is incomplete | deterministic harness for ambiguity, ordering, partials, outage, and restart |
| Shadow parity corpus | generation is active but has zero real decisions | identified market-open and market-closed observations |
| Trading Core network | DNS-only egress | evidence-only edge first; public 443 plus broker-host validation only at Remote acceptance |
| acceptance cadence | infrastructure subfeatures were separately verified/deployed | integrate T1–T5, then execute one completion gate |

## 14. Conditional later boundaries

Strategy Research remains in Edge unless measurements show sustained resource contention, an
independent release need, or provider/research failure affecting control-plane availability. If it
is extracted, it owns strategy/signal/optimization lifecycle and calls Market Data, workers, ML
Training, and Trading Core through the already defined contracts; Edge remains the user gateway.

Reporting/Notifications remains absent until a real delivery channel is enabled. Its future Pod may
only pull immutable Trading Core activity by cursor into its own projection/inbox. It can never be a
dependency of order execution.

## 15. Design review checklist

Before implementation resumes, confirm:

- every existing table and background worker maps to exactly one target owner;
- every allowed network edge appears in both the call graph and NetworkPolicy table;
- every financial sequence terminates at one Trading Core transaction boundary;
- every retry has a stable identity and an unambiguous effect policy;
- every compatibility store is marked projection, cache, or rollback snapshot;
- every secret has one consumer boundary and a rotation class;
- every deployment wave has an observable stop condition and preserved rollback state;
- the target fits the measured CPU, memory, and storage budget of the current server.
