# 0069 — Adopt evolutionary service extraction

- Status: Accepted for architecture planning; implementation requires separate approval
- Date: 2026-08-23
- Baseline: `main` at `0e0acb6`

## Context

StockTrader has completed the first modular-monolith boundary pass, but its backend is still one
deployable process. The process hosts the HTTP API and eleven long-running workers, compiles one web
project, and persists eighteen entity sets through one SQLite `AppDbContext`. K3s deliberately runs
one API replica with `Recreate` because the database and ML models live on one host-mounted volume.

The product now contains workloads with materially different operational characteristics:

- live order execution and risk decisions are low-volume, latency-sensitive, and safety-critical;
- market-data ingestion is provider-bound, bursty, and storage-heavy;
- preview and backtest are interactive deterministic calculations;
- optimization and ML training are CPU-heavy, cancellable batch jobs;
- reports and notifications tolerate delay and replay.

A full microservice rewrite would turn in-process calls and SQLite transactions into network and
distributed-consistency problems before StockTrader has durable messaging, service-owned stores,
cross-service observability, or multi-node availability. Conversely, keeping every workload in one
process indefinitely allows batch CPU/memory pressure and provider failures to affect live trading.

## Decision

StockTrader will use **evolutionary service extraction**, not a big-bang MSA rewrite. The modular
monolith remains the production baseline until a candidate passes the extraction gates in
`../msa-transition-roadmap.md`. Documentation, measurement, and contract design are authorized by
this ADR; runtime, database, deployment, and code separation are not authorized without an explicit
implementation decision.

The target topology is intentionally small:

| Boundary | Responsibility and owned state | Initial deployment decision |
| --- | --- | --- |
| Edge and Control API | Authentication, user/account administration, settings, metadata, API composition, audit entry | Remains the desktop-facing API |
| Trading Core | Live signal acceptance, risk gate, broker commands, order reconciliation, positions, fills, realized trades | Remains one strongly consistent boundary; extracted only after the platform gates pass |
| Market Data | Provider sessions, streaming and REST ingestion, normalized bars, evidence, calendars, data-quality status | First durable data-service candidate |
| Strategy Research | Strategy documents, compilation, preview, ordinary backtest, analysis queries | Remains cohesive with the deterministic engine |
| Optimization Workers | Candidate evaluation and walk-forward/Monte Carlo batch execution | First process-extraction candidate; owns no trading authority |
| ML Training | Causal training samples, model build/validation, model manifests and publication | Later batch-worker candidate |
| Reporting and Notifications | Daily projections and external delivery from immutable events/read models | Optional low-risk event consumer |

Optimization Workers may be independently deployed while Strategy Research retains job ownership.
This is deliberate: workers receive immutable evaluation jobs and return versioned results, so they
do not become a second owner of strategies or optimization lifecycle state.

Trading Core must not be decomposed into separate risk, order, position, or broker services. The
risk check, durable order claim, broker evidence, fill application, position mutation, and realized
trade mutation are one safety boundary. Network calls are not permitted inside that state
transition except the unavoidable broker adapter call, which is coordinated by a durable request
state and reconciliation.

## Preserving trading semantics

Every deployable that evaluates a strategy must consume the same versioned engine artifact and the
same immutable inputs. A `StrategyExecutionArtifact` contract will identify at least:

- the versioned `StrategyDocument` and its content hash;
- compiler, engine, indicator-catalog, pattern-catalog, calendar, and cost-model versions;
- timeframe, adjustment mode, warmup, execution timing, and supported live capabilities.

Market data crossing a boundary must carry `MarketDataEvidence`: provider, market, symbol,
timeframe, adjustment mode, calendar version, requested interval, completed-bar cutoff, observed
interval, and a stable content identity. A result must preserve the artifact and evidence identities
that produced it.

Preview, backtest, and live release gates will run a shared conformance corpus. The corpus must prove:

1. identical compiled rule meaning and indicator warmup;
2. no future-bar access and an explicit completed-bar cutoff;
3. next-open orders consume their entry bar;
4. conservative ordering when intrabar price order is unknowable;
5. costs change equity at execution time;
6. identical entry geometry, scaling, exit reason, and portfolio transition where the environments
   receive the same observable evidence;
7. live rejection of artifact versions or features without proven parity.

Copying engine source into services, independently reimplementing catalogs, or translating strategy
rules into service-specific DTO semantics is forbidden. A version mismatch fails closed for live
trading and is explicit result metadata for research.

## Data ownership and consistency

Each extracted service will own its schema and migration history. No service may query another
service's tables, share an SQLite file, or use a cross-service foreign key. The current database will
not be physically split until ownership, compatibility readers, reconciliation, backup, and rollback
have been proven for that extraction slice.

The intended ownership map is:

| Aggregate | Owner |
| --- | --- |
| Users, encrypted account configuration, global settings, audit log | Edge and Control API |
| Bars and provider/data-quality evidence | Market Data |
| Strategy documents | Strategy Research |
| Optimization job lifecycle and accepted results | Strategy Research |
| Signals accepted for execution, recommendations, orders, positions, scaling executions, trades | Trading Core |
| Financial snapshots and import runs | Strategy Research until an independent Research Data boundary is justified |
| Model manifests and publication state | ML Training; Trading Core receives only approved immutable model artifacts |

Strong consistency is local to the owning service. Cross-service propagation is at-least-once and
idempotent, using a transactional outbox at the producer and an inbox/deduplication key at the
consumer. Event identities are stable business identities, not transport delivery IDs. Consumers
must tolerate duplicate, delayed, and out-of-order delivery. Financial state is never inferred from
an event when broker reconciliation is the authoritative evidence.

Synchronous HTTP or gRPC is reserved for bounded queries and commands whose caller needs an
immediate answer. Durable work, market-data availability, model publication, and reporting use
asynchronous messages. Trading Core must fail closed when risk, account, artifact, or required bar
evidence is unavailable; research requests may return a typed incomplete/degraded result.

No message broker product is selected yet. The first implementation ADR must compare operational
cost and recovery behavior on the actual single-node K3s environment. Broker adoption is not a
prerequisite for extracting a stateless optimization worker if the existing durable job store can
provide leasing, idempotency, heartbeats, cancellation, and result acceptance.

## Reliability, observability, security, and operations

Before the first production extraction, every request, job, event, strategy artifact, market-data
evidence set, broker order, and reconciliation attempt must have correlated structured identifiers.
Service-level signals must cover latency, error rate, queue age, duplicate count, stale data,
artifact mismatch, broker reconciliation lag, and last successful financial effect. Logs alone are
not an availability strategy.

Internal calls require workload identity or independently rotated credentials, least-privilege
authorization, encryption in transit outside a strictly controlled node-local network, and no secret
material in events. Broker credentials are available only to Control API account administration and
the Trading Core broker adapter. Research and worker services never receive them.

Deployments need independent health/readiness checks, resource limits, disruption and rollback
rules, schema compatibility, and tested backup/restore. A service is not considered independently
available while all of its instances, database, broker, and storage remain on one physical node.

## Extraction gates

A boundary may be extracted only when all of these are true:

- measured contention, release cadence, scaling, isolation, security, or ownership evidence justifies
  the additional network and operational cost;
- one team or operator can state the data owner and recovery authority without ambiguity;
- contracts are versioned and have consumer, compatibility, idempotency, and failure-path tests;
- the source process can run in shadow/dual-read mode without dual financial writes;
- dashboards, alerts, runbooks, backup, restore, rollback, and reconciliation are exercised;
- the affected trading conformance suite passes against both old and candidate paths;
- a named ADR authorizes the extraction and records measured baseline and rollback thresholds.

## Consequences

This decision permits workload isolation without surrendering deterministic trading semantics. It
also means the desired end state may contain fewer deployables than a conventional MSA diagram. A
boundary that has no measured reason to cross the process boundary remains an internal module.

There will be temporary compatibility readers and replicated read models. They are migration tools,
not shared ownership. The cost is additional contract, telemetry, reconciliation, security, data
migration, and on-call work for every extracted service. The benefit must be demonstrated per slice.

## Rejected alternatives

### Rewrite all modules as services now

Rejected because the current single SQLite store, single-node K3s, and in-process workflows cannot
be converted safely in one change. It would also violate the staged-refactoring discipline.

### Split by existing folder or entity

Rejected because technical layers and tables are not business consistency boundaries. In
particular, risk, orders, positions, and fills must not become network-separated CRUD services.

### Keep one process permanently

Rejected as a permanent rule because optimization, ML, ingestion, and provider instability can
eventually require independent resource and failure isolation from live trading.

### Share the SQLite database between services

Rejected because it creates distributed deployment without data ownership, independent recovery,
or safe concurrency.
