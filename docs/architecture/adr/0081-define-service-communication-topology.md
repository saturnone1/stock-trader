# 0081 — Define the service communication topology before further extraction

- Status: Proposed — awaiting design review
- Date: 2026-08-27

## Context

Optimization Worker, Market Data, ML Training, and the candidate Trading Core already run as
independent Kubernetes workloads. Their boundaries were extracted safely, but transport, identity,
retry, and failure behavior were decided one service at a time. That made each later boundary repeat
architecture work and encouraged small infrastructure checkpoints instead of completing one Pod as
a coherent unit.

Production runs on a low-power, single-node K3s server. A message broker, service mesh, or separate
database cluster would consume material resources without creating a second physical failure domain.
The trading invariants also require one financial authority and exact evidence, not a network of
independently writable order, risk, and position services.

## Decision

The detailed current/target inventory, data ownership, contract registry, end-to-end sequences,
failure matrix, security rules, and deployment waves are maintained in the
[MSA target blueprint](../msa-target-blueprint.md). They are normative for implementation.

The intended production topology is six workload types. Strategy Research remains inside Edge until
measured isolation value justifies Stage 6; Reporting/Notifications remains deferred while delivery
channels are disabled.

```text
Desktop
  |
  | public HTTPS + user session
  v
Edge / Control API ---------------------> Market Data (7443)
  |          |                              ^
  |          +--------------------------+   |
  |                                     |   | exact evidence lookup
  | leases/results                     jobs |
  v                                     v   |
Optimization Workers (pull)          ML Training (8443)
  |
  | no financial route
  |
Edge / in-process Strategy Research -----> Trading Core (9443)
                                             |
                                             | broker HTTPS, Remote only
                                             v
                                         Broker APIs
```

The diagram shows logical calls, not shared storage. The additional direct
`Trading Core -> Market Data` edge is enabled before Remote authority so Trading Core can verify an
evidence identity independently of the command producer and obtain the bounded next completed-bar
window needed to protect an open position when Edge is unavailable. It reads persisted canonical
evidence only and is not a provider or general research query path. Until then Trading Core keeps
DNS-only egress.

### Workload ownership

| Workload | Owns | Persistent store | Must not own |
| --- | --- | --- | --- |
| Desktop | Interaction state and presentation | none | catalogs, trading policy, credentials |
| Edge / Control API | Authentication, user commands, aggregation, settings, and currently Strategy Research | application SQLite | market bars, model artifacts, remote optimization compute, canonical financial state after cutover |
| Optimization Worker | Stateless deterministic candidate evaluation | none | job lifecycle, accepted result, providers, orders |
| Market Data | Provider access, normalized bars, corrections, evidence identities | market-data SQLite | broker account state, strategies, trades |
| ML Training | Training queue and immutable model publications | training SQLite plus artifact volume | inference policy, strategies, orders |
| Trading Core | Account generation, risk, commands, broker evidence, reconciliation, positions, fills, trades | trading-core SQLite | strategy authoring, broad historical research, notifications |

Risk, order lifecycle, positions, fills, trades, reconciliation, and broker order adapters remain one
Trading Core deployment boundary. They must not be split into separate Pods. Broker adapters are
outbound adapters inside Trading Core, not independently authoritative services.

### Transport patterns

Only three transport patterns are permitted unless a later ADR replaces this decision:

1. **Synchronous private command/query** — JSON over HTTPS with workload mTLS and endpoint-role
   authorization. Used by Edge to Market Data, ML Training, and Trading Core, and by Trading Core
   for exact Market Data evidence verification.
2. **Durable lease pull** — workers claim, heartbeat, and return immutable work through an internal
   Edge endpoint. The canonical owner retains the queue and rejects stale lease generations.
3. **Cursor pull** — a future Reporting/Notifications consumer reads immutable activity events by
   monotonic cursor with inbox deduplication. It never participates in a financial transaction.

No Kafka, RabbitMQ, service mesh, shared database table, cross-service foreign key, or distributed
transaction is introduced on the current server. A broker may be reconsidered only when measured
event volume or consumer count exceeds cursor-pull capacity and its resource budget is documented.

### Allowed call graph

| Caller | Callee | Purpose | Write authority | Failure behavior |
| --- | --- | --- | --- | --- |
| Desktop | Edge | User commands and aggregated queries | Edge validates user intent | visible request failure; never bypass Edge |
| Edge | Market Data | Bounded provider/range/latest queries | Market Data only | research degrades or fails; no invented bars |
| Worker | Edge internal endpoint | Claim/heartbeat/result | Edge owns job/result acceptance | lease expires and is reclaimed; duplicate result is idempotent |
| Edge | ML Training | Submit/cancel/status/publication | ML Training owns queue/publication | training can stop without changing live inference artifact |
| Edge/Research | Trading Core | Projection, account generation, authority, commands, canonical reads | Trading Core only in Remote | command fails closed; no automatic Local fallback in Remote |
| Trading Core | Market Data | Verify named evidence/hash before execution | Market Data only | reject stale, missing, corrected, or mismatched evidence |
| Trading Core | Broker | Submit/query/cancel and collect evidence | Trading Core only in Remote | ambiguous submission remains reconciling; never blind-retry |
| Reporting consumer | Trading Core | Pull immutable activity by cursor | consumer projection only | retry/deduplicate; cannot block trading |

Every other Pod-to-Pod route is denied by NetworkPolicy. In particular, Optimization Worker and ML
Training have no route to Trading Core or broker endpoints, Market Data has no route to Trading Core,
and Desktop cannot call an internal service directly.

### Contract and identity rules

Every cross-Pod payload has an explicit contract version. Breaking changes use a parallel versioned
endpoint or compatibility reader; a producer is not upgraded past the consumer's accepted version.
Financial and work messages additionally carry, as applicable:

- stable message/command or job identity and payload hash;
- correlation and causation identity;
- producer and occurred/observed UTC time;
- idempotency identity and lease or authority generation;
- account/configuration generation;
- strategy artifact/compiler/engine version and content hash;
- Market Data evidence identity, timeframe, adjustment, calendar, cutoff, completeness, revision,
  and content hash;
- expiry for pre-effect commands.

The receiver validates the envelope before durable acceptance. A command timeout after a possible
external effect is an unknown outcome to reconcile, not permission to create a new identity.

### Authentication and secret boundaries

Each caller receives its own client certificate with an exact workload-role identity. The server
validates the private CA, client-auth EKU, identity, and operation allow-list. NetworkPolicy is the
second control. A shared header secret stored beside the certificate adds rotation work but does not
create a meaningfully independent compromise boundary, so existing shared transport secrets are
compatibility-only and are removed after cert-role migration.

Account-encryption and broker credentials are never reused as transport authentication. Only
Trading Core receives broker trading credentials. Market Data receives only provider-data
credentials. Optimization Worker and ML Training receive neither. A one-sided certificate rotation
must isolate only that dependency and must never transfer financial authority. Encryption-key
rotation is a data migration and cannot be implemented as a Pod restart.

### Timeouts, retries, and availability

- Queries may retry only with bounded exponential backoff and jitter within the caller's deadline.
- Commands may retry only with the same stable identity and payload hash.
- Broker submission is never automatically retried after an ambiguous response; reconciliation
  queries the broker using the durable client identity.
- Readiness represents whether a Pod can safely perform its owned function. A non-authoritative
  Shadow dependency error is reported in health without taking Local authority down.
- No circuit breaker may silently substitute stale or Local financial behavior for Remote authority.
- Operational timeout, concurrency, and retry values are validated typed options, not copied magic
  numbers.

### Kubernetes ports and policy

| Service | Cluster port | Ingress allow-list | Egress allow-list |
| --- | ---: | --- | --- |
| Edge public | 3000 | Desktop/Ingress | Market Data, ML Training, Trading Core, DNS, approved external auth/data adapters still owned by Edge |
| Edge worker control | 3443 | Optimization Worker | same Pod as Edge |
| Market Data | 7443 | Edge; Trading Core evidence client after its identity is added | DNS plus approved data-provider endpoints |
| ML Training | 8443 | Edge | DNS only unless an artifact backend is approved |
| Trading Core | 9443 | Edge; future Strategy Research identity | DNS in Projection/Shadow; Market Data evidence plus broker HTTPS with application host validation only in Remote |

Health and metrics ports remain cluster-internal. NetworkPolicy is generated from this matrix, and a
deployment is incomplete if its manifest permits an undocumented edge.

## Completion order

Work proceeds by deployable service, not by small cross-cutting change:

1. Finish Trading Core as one Pod boundary: certificate-role authorization, header-secret
   retirement, account-encryption migration, bounded Market Data execution evidence, controlled
   broker-evidence convergence, load, Remote single-writer cutover, and reconciled rollback.
2. Run one complete Trading Core verification batch and production acceptance rehearsal.
3. Re-measure Edge. Extract Strategy Research only if CPU, release cadence, or failure isolation
   crosses its documented trigger; otherwise keep it as an Edge module.
4. Add Reporting/Notifications only after an actual channel is enabled and needs independent
   delivery recovery.

For each item, implementation may use focused compiler checks while being built, but the complete
backend/desktop/K3s verification suite runs once at the service completion gate rather than after
each internal edit.

## Consequences

The topology stays small enough for the server, makes every allowed network edge reviewable, and
keeps one durable financial owner. It avoids infrastructure whose operating cost exceeds its current
benefit. Trading Core Remote completion remains deliberately demanding because it moves real-money
authority, but the remaining work is now one service batch rather than a sequence of separately
accepted micro-checkpoints.

This ADR does not claim high availability: every workload still shares one physical node. It also
does not authorize Strategy Research or Reporting extraction before their measured triggers.
