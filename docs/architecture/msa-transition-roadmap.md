# Evolutionary MSA transition roadmap

This roadmap turns [ADR 0069](adr/0069-adopt-evolutionary-service-extraction.md) into decision gates.
It does not authorize implementation. The production baseline remains the modular monolith at
`main` commit `0e0acb6` until a separately approved extraction ADR says otherwise.

## Baseline and feasibility finding

The current backend is not yet operationally ready for broad service decomposition:

- one .NET web project contains the API, application code, adapters, and eleven hosted workers;
- one `AppDbContext` owns eighteen entity sets in one SQLite database;
- the API runs as one `Recreate` replica with one host-mounted data/model volume;
- there is no durable inter-service transport, outbox/inbox, distributed trace context, or
  service-owned database backup/restore path;
- API and Desktop are separate deployments, but backend workloads share one failure and resource
  domain.

MSA is feasible as selective extraction. It is not presently justified as a full rewrite. The
expected value is highest where a workload is compute-heavy, provider-failure-prone, or independently
scalable and has no authority to create financial effects.

## Target dependency and communication model

```text
Desktop
   |
Edge / Control API ------------------------------+
   |                                              |
   | synchronous user commands and queries       | read projections
   v                                              v
Strategy Research ---- immutable jobs ----> Optimization Workers
   |        ^
   |        | versioned bars + MarketDataEvidence
   v        |
Market Data Service
   |
   +---- completed-bar/data-ready events --------> Strategy Research / ML Training
   |
   +---- versioned evidence query ---------------> Trading Core

Strategy Research -- approved StrategyExecutionArtifact --> Trading Core
Trading Core ------ immutable activity events ------------> Reporting / Notifications
Trading Core <----- broker evidence -----------------------> Broker adapters
```

The arrows do not imply shared databases. User-facing aggregation belongs at the Edge API; financial
commands terminate at Trading Core. Research cannot directly place an order.

## Implementation language policy

Extracted compute and orchestration services default to **F# on .NET**. They reference shared C#
contract and deterministic-engine assemblies instead of reimplementing strategy, indicator, fill,
cost, or portfolio semantics. This keeps service hosts concise while preserving one executable
meaning across preview, backtest, optimization, and live trading.

The ASP.NET application project is not a shared library. Before the first F# host is introduced,
contracts and deterministic engine code must compile as independent .NET libraries with architecture
tests enforcing their dependency direction. Engine-free projection/notification services may
evaluate Go in an extraction-specific ADR, but language diversity must demonstrate a material line
count or operational benefit and must not duplicate a trading catalog or policy.

## Service scorecard

Scores are relative to the current baseline: 1 is low and 5 is high.

| Candidate | Isolation/scaling value | Consistency risk | Operational cost | Recommended order |
| --- | ---: | ---: | ---: | --- |
| Optimization worker process | 5 | 1 | 2 | 1 |
| Market Data service | 5 | 3 | 4 | 2 |
| ML Training worker | 4 | 2 | 3 | 3 |
| Reporting/Notifications consumer | 3 | 1 | 3 | 4, only if delivery isolation is needed |
| Trading Core service | 5 | 5 | 5 | 5, after platform proof |
| Strategy Research service | 3 | 4 | 4 | 6 or remain in Edge deployment |
| Separate Risk/Order/Position services | 1 | 5 | 5 | Do not split |

## Stage 0 — Measure and define extraction triggers

No runtime topology changes are allowed in this stage.

Deliverables for an implementation proposal:

- CPU, memory, GC pause, request latency, worker duration, provider failure, database lock, and queue
  age baselines separated by workload;
- explicit service-level objectives for live reconciliation, data freshness, preview/backtest,
  optimization, and reporting;
- a table-to-owner and command/event ownership register;
- recovery point/time objectives and a tested current SQLite/ML-model restore exercise;
- correlation and business identity conventions;
- a cost baseline for the current single-node deployment.

Exit gate: at least one candidate has a measured problem and a threshold at which extraction is
cheaper and safer than in-process resource governance.

## Stage 1 — Contract and conformance foundation

This remains design work until separately approved.

Define versioned schemas for:

- `StrategyExecutionArtifact` and capability compatibility;
- `MarketDataEvidence`, bar batches, completeness and correction notices;
- optimization lease, heartbeat, cancellation, immutable evaluation input, and result acceptance;
- model artifact publication and rejection;
- trading activity events with stable source signal, broker order, position, fill, and trade IDs;
- standard event envelope, causation, correlation, schema version, occurred-at, producer, and
  idempotency identity.

Define the shared conformance corpus for preview, backtest, optimization workers, and live execution.
It must cover future-bar exclusion, entry-bar consumption, conservative intrabar ordering, execution
cost timing, scaling counts, calendar/DST, adjustment modes, and version mismatch behavior.

Exit gate: contract compatibility and semantic conformance can be proven without deploying a second
production writer.

## Stage 2 — Extract stateless Optimization Workers

Why first: this creates useful CPU and failure isolation without moving financial authority or
owning canonical strategy/job data.

Approved implementation would:

- keep optimization job lifecycle and accepted results in Strategy Research;
- lease immutable jobs to workers with bounded concurrency and resource quotas;
- include engine/artifact/data-evidence versions in every job and result;
- accept a result only while its lease, cancellation generation, and input hash still match;
- support retry and duplicate result submission without duplicate accepted results;
- compare shadow-worker results byte-for-byte or tolerance-by-tolerance with the in-process path.

Rollback: stop leasing to remote workers and resume the in-process executor. No data migration or
financial write reversal is required.

Exit gate: equivalent results, cancellation, crash recovery, stale-result rejection, resource
isolation, and rollback are proven under load.

## Stage 3 — Extract Market Data

Why second: ingestion and historical storage are a distinct provider/data-quality domain, but data
corrections and evidence identity make it riskier than stateless compute.

Approved implementation would:

- give Market Data exclusive write ownership of normalized bars and provider evidence;
- migrate by backfill plus change capture, then shadow-read and compare ranges/checksums;
- expose bounded range queries and publish data-ready/correction events;
- preserve explicit provider, market, timeframe, adjustment, calendar, cutoff, and completeness;
- make consumers cache only by evidence identity and invalidate on correction;
- keep broker account credentials out of this service; only market-data provider credentials enter.

Trading Core fails closed when required evidence is stale, incomplete, corrected but unacknowledged,
or served under an unsupported calendar/adjustment version. Research returns the exact evidence and
degradation in its result.

Rollback: consumers switch to the compatibility reader while the old store remains read-only and
reconcilable. Dual writes are not the steady state.

Exit gate: historical parity, correction replay, provider outage, stale-data rejection, backup,
restore, and rollback are proven for every supported timeframe.

## Stage 4 — Extract ML Training and optional event consumers

ML Training may run independently after model publication is an immutable, signed or hashed artifact
promotion rather than a shared-directory mutation. Trading Core loads only approved manifests whose
feature schema, label map, engine compatibility, and content hash match.

Reporting and notification may consume immutable activity events after replay, deduplication,
timezone windows, redelivery, and dead-letter recovery are proven. Reporting projections are never
authoritative inputs to trading.

Exit gate: a worker or consumer outage cannot change live trading state, and replay cannot duplicate
financial or external-notification effects beyond the documented delivery policy.

## Stage 5 — Isolate Trading Core

This stage is justified only if live trading needs stronger resource, release, security, or failure
isolation and Stages 2–4 have proven the platform and runbooks.

Trading Core owns signal acceptance for execution, risk state, broker order lifecycle,
reconciliation, positions, scaling state, and realized trades. These stay in one database and one
deployment boundary. Account configuration may be administered by Control API, but Trading Core
receives a versioned secret reference/configuration generation and must not execute against stale or
ambiguous account state.

Migration uses one financial writer at a time:

1. build and compare read-only projections;
2. replay recorded decisions against the shared conformance corpus;
3. run shadow decisions without broker submission or durable financial mutation;
4. stop the old writer and reconcile all outstanding broker orders;
5. record a cutover generation and enable the new writer;
6. continuously reconcile broker evidence, old/new stores, and cutover identities;
7. roll back only after disabling the new writer and reconciling every in-flight order.

Exit gate: failover drills prove there is never more than one order authority, open orders survive
process loss, every broker fill converges to one durable state, and live feature/version mismatches
fail closed.

## Stage 6 — Re-evaluate Strategy Research and Edge

Do not extract a service merely to complete a diagram. Separate Strategy Research from Edge only if
interactive compute, release cadence, ownership, or scaling evidence justifies it. Otherwise it may
remain an internal module in the Edge deployment while using Market Data and remote Optimization
Workers.

Exit gate: the retained topology has a documented reason for every network boundary and every
module left in-process.

## Cross-cutting release gates

Every extraction release must provide:

- a new extraction-specific ADR with observed baseline, decision, failure modes, and rollback;
- versioned producer/consumer contracts and compatibility window;
- transactional outbox/inbox or an equally strong documented idempotency mechanism;
- no shared database access or cross-service foreign keys;
- readiness based on dependencies needed for safe behavior, not merely process liveness;
- traces and metrics that follow user request, job, strategy artifact, data evidence, and broker order;
- least-privilege identity, secret isolation, encryption policy, and audit trail;
- load, chaos, duplicate, delay, reordering, backup/restore, and rollback evidence;
- the complete trading semantic conformance suite;
- a cost comparison against the modular-monolith baseline.

## Stop conditions

Pause an extraction and return to the last single-owner state if any of these occurs:

- preview, backtest, optimization, and live semantics cannot be tied to one artifact/version set;
- market-data completeness or correction identity is ambiguous;
- two processes can submit or settle the same financial intent;
- recovery depends on manually editing multiple databases;
- required observability cannot distinguish delay from data loss;
- operating cost or failure recovery exceeds the measured benefit;
- the single-node K3s environment is presented as high availability without independent compute and
  storage failure domains.

## Decisions still required before implementation

The following are intentionally unresolved and require evidence-led ADRs:

- transport and schema technology;
- database engine and per-service backup topology;
- workload identity and internal TLS mechanism;
- trace/metric/log stack and retention;
- whether K3s remains single-node or gains independent failure domains;
- exact SLOs, RPO/RTO, capacity thresholds, and cost ceiling;
- the first candidate whose measurements actually justify extraction.
