# Trading Core isolated acceptance design

- Status: Proposed A1/A2 design baseline; no implementation, test run, or deployment authorized
- Parent contracts: [Trading Core acceptance and authority contracts](trading-core-acceptance-contracts.md)
- Parent decision: [ADR 0082](adr/0082-define-trading-core-acceptance-and-single-writer-cutover.md)
- Date: 2026-09-01

## 1. Goal

This design defines the isolated K3s environment that proves Trading Core replay, broker-failure
convergence, Pod-loss recovery, and resource behavior without creating or changing production
financial state. It completes the design scope of ADR 0082 packages A1 and A2.

The environment must satisfy all of these properties simultaneously:

- run the same financial worker, store, engine, migrations, scheduler, contracts, and HTTP boundary
  used by the production candidate;
- keep the scripted broker, virtual clock controls, fixtures, and fault injection out of the
  production image;
- preserve Trading Core and broker state independently across Pod deletion;
- consume real, read-only Market Data evidence through the production contract and dedicated role;
- mount no production volume and receive no production broker, user, or encryption Secret;
- execute one complete scenario catalog against one named compatible image set;
- leave one deterministic, redacted acceptance manifest and no permanent service workload.

## 2. Build and composition boundaries

### 2.1 Target project layout

The future implementation separates reusable runtime behavior from host composition:

```text
StockTrader.TradingCore.Runtime (F# library)
  store + migrations + workers + reconciliation + position scheduler + standard HTTP operations
  depends on ServiceContracts, Engine, TradingCore policies, SQLite, purpose-specific ports

StockTrader.TradingCore.BrokerPorts (C# contract/policy library)
  broker requests/evidence + risk gate + ITradingBroker/ITradingBrokerFactory; no provider client

StockTrader.TradingCore.AlpacaAdapter (production-only adapter library)
  existing Alpaca transport moved mechanically; no trading policy

StockTrader.TradingCoreService (F# production host)
  production configuration + mTLS + System TimeProvider + approved broker factory

StockTrader.TradingCoreAcceptanceHost (F# acceptance host)
  acceptance configuration + mTLS + controlled TimeProvider + scripted-broker client factory

StockTrader.TradingCoreBrokerEmulator (F# ephemeral service)
  durable scripted external-broker journal; no trading policy

StockTrader.TradingCoreAcceptanceDriver (F# Job)
  scenario orchestration + assertions + manifest construction; no trading policy
```

The existing runtime files move mechanically into the Runtime library; financial rules are not
translated or copied. The current broker port/risk policy separates from the concrete Alpaca class,
and the existing Alpaca implementation moves mechanically into the production-only adapter. The
production host references that adapter; Runtime and the acceptance host do not.

Production and acceptance images are built in the same invocation from the same commit. Their
manifest records byte hashes of Runtime, ServiceContracts, Engine, BrokerPorts, and TradingCore
policy assemblies, and acceptance fails unless those shared hashes match.

The production image contains neither the acceptance host nor emulator/driver assemblies. The
acceptance image contains no Alpaca or LS credential binding and cannot instantiate an approved
production broker adapter.

### 2.2 Required runtime ports

The shared workers depend on narrow injected ports:

| Port | Production binding | Acceptance binding |
| --- | --- | --- |
| broker factory by validated account | approved Alpaca/LS adapter factory | scripted broker client factory |
| clock and delay | `TimeProvider.System` | controlled scenario time provider |
| Market Data evidence | dedicated mTLS evidence client | same client and contract with acceptance role |
| durable financial store | production Trading Core SQLite path | per-scenario isolated SQLite PVC |

The broker factory is the only permitted construction point for `ITradingBroker`. Shared workers
must not instantiate `AlpacaTradingBroker`, read environment variables, or choose Paper/Live mode.
The controlled clock changes observation time only; it cannot replace market bars, evidence hashes,
or exchange-calendar policy.

The dependency direction is fixed:

```text
Production host -> Runtime -> BrokerPorts <- AlpacaAdapter
Acceptance host -> Runtime -> BrokerPorts <- ScriptedBrokerClient
Driver/Emulator -------------------------> acceptance contracts only
```

Runtime never references either adapter. This is an enforceable project-reference rule rather than
a naming convention.

### 2.3 Standard and acceptance-only surfaces

The acceptance Trading Core Pod exposes the same standard mTLS command, status, portfolio, and
authority contracts as production. Its acceptance-only time-control surface is compiled solely into
the acceptance host, listens on a separate port, accepts only the driver certificate role, and is
unreachable from outside the ephemeral namespace.

Broker scripts are sent only to the Broker Emulator control surface. Trading Core has no endpoint
that selects a broker response or mutates a test result. The driver observes outcomes through
standard Trading Core queries plus read-only emulator journal queries.

## 3. Ephemeral K3s topology

One bounded acceptance run uses a generated namespace named
`stocktrader-acceptance-<lowercase-run-id>`:

```text
Production namespace: stocktrader
  Market Data :7443 (read-only evidence operation only)
          ^
          | dedicated acceptance evidence role + mTLS
----------|------------------------------------------------------------------
Ephemeral namespace: stocktrader-acceptance-<run-id>

  Acceptance Driver Job
      | standard commands/status :9443
      | acceptance time control :9543
      | emulator plan/journal :10443
      | scoped Pod delete/watch for loss scenarios
      v
  Trading Core Acceptance Deployment (1 replica, Recreate)
      | own PVC: tc-<scenario-id>
      | scripted broker API :10443
      +----------------------------> Broker Emulator Deployment (1 replica)
                                      own PVC: broker-<scenario-id>

  Manifest PVC: run evidence only; copied out and hash-checked before cleanup
```

Only one scenario pair is active at a time on the low-power node. The driver replaces the
scenario-scoped Trading Core and emulator objects between cases, so state cannot leak between
scenarios and no reset/fabrication endpoint is needed. The run-level driver and manifest volume
survive those replacements.

### 3.1 Kubernetes object inventory

| Object | Count/lifetime | Purpose |
| --- | --- | --- |
| Namespace | one per run | hard isolation and exact cleanup boundary |
| ResourceQuota + LimitRange | one each | cap aggregate CPU, memory, PVC count, and Pod count |
| default-deny NetworkPolicy | one | deny all ingress/egress before explicit edges |
| ServiceAccounts | driver, Trading Core, emulator | distinct identity; token automount disabled except scoped driver token |
| Role/RoleBinding | one | driver may get/list/watch/delete only run-namespace acceptance Pods and manage scenario objects |
| mTLS Secrets | ephemeral role/server certificates | generated for this run; no preserved production private key |
| synthetic encryption Secret | one | unique random key and `acceptance-<run-id>` generation |
| scenario ConfigMaps | one immutable object per scenario | non-secret plan metadata and expected hashes |
| Trading Core Deployment/Service/PVC | one active set | real runtime path and restart-persistent candidate state |
| Broker Emulator Deployment/Service/PVC | one active set | independently durable external-broker state |
| Driver Job + manifest PVC | one per run | orchestration, assertions, append-only results |

The namespace uses Pod Security `restricted`, non-root users, read-only root filesystems, dropped
capabilities, runtime-default seccomp, bounded `/tmp`, and no host network, PID, IPC, privileged
container, hostPath, or production PVC reference. Scenario PVC names include both run and scenario
identities and use the existing local storage class only inside the generated namespace.

### 3.2 Resource envelope

The run starts only while ML Training and full Optimization Worker computation are paused by their
existing admission owner. Initial acceptance ceilings are:

| Workload | CPU request / limit | Memory request / limit |
| --- | ---: | ---: |
| Trading Core acceptance | 50m / 500m | 96Mi / 256Mi |
| Broker Emulator | 25m / 150m | 48Mi / 128Mi |
| Driver | 25m / 250m | 64Mi / 192Mi |

The namespace quota permits at most four running Pods, four PVCs, 900m CPU limits, and 640Mi memory
limits. The accepted-load scenario stays within the production Trading Core limit; raising limits to
make a failure disappear invalidates that scenario.

## 4. Identity, Secret, and network design

### 4.1 Workload roles

| Caller role | Destination | Allowed operations |
| --- | --- | --- |
| `acceptance-driver.<run>.stocktrader.internal` | acceptance Trading Core | standard commands/queries and acceptance time control |
| `acceptance-trading-core.<run>.stocktrader.internal` | production Market Data | execution-evidence verify/read only |
| `acceptance-trading-core.<run>.stocktrader.internal` | Broker Emulator | broker operations only |
| `acceptance-driver.<run>.stocktrader.internal` | Broker Emulator | load immutable plan and read journal only |

Production Edge and Trading Core certificates are not copied. The acceptance CA and leaf Secrets are
generated for the run, short-lived, and deleted with the namespace after artifact export.

### 4.2 NetworkPolicy allow-list

The effective policy is default deny plus these edges:

| Source | Destination | Port | Notes |
| --- | --- | ---: | --- |
| driver | acceptance Trading Core | 9443 | standard mTLS operations |
| driver | acceptance Trading Core | 9543 | acceptance-only clock control |
| driver | emulator | 10443 | plan and journal mTLS operations |
| acceptance Trading Core | emulator | 10443 | scripted `ITradingBroker` transport |
| acceptance Trading Core | production Market Data | 7443 | exact evidence operations only |
| run Pods | kube-dns | 53 UDP/TCP | DNS only |

There is no public `443` egress. The production Market Data policy gains one conjunctive
`namespaceSelector` plus `podSelector` edge for labeled acceptance Trading Core Pods; its mTLS role
authorization remains the operation-level guard. The acceptance role must receive `403` from every
provider, ingestion, correction, and mutation route.

### 4.3 Secret absence proof

The driver records Secret names, key-name sets, volume references, environment references, and
ServiceAccount identities, never values. Acceptance fails if any Pod specification references:

- a production broker credential Secret;
- the production Trading Core encryption Secret;
- a user/session/authentication Secret;
- the production Trading Core client or server private key;
- any production financial PVC or hostPath.

The manifest stores the derived absence result and the redacted object-spec hash.

## 5. Broker Emulator design

### 5.1 Responsibility

The emulator models only externally observable broker protocol behavior. It does not evaluate risk,
strategies, prices, position rules, or expected Trading Core outcomes. Its state consists of account
evidence, positions, orders, fills, the immutable scenario plan, and an append-only call/effect
journal.

Its SQLite PVC is independent from the Trading Core PVC. Deleting Trading Core therefore preserves
whether the broker accepted an order, exactly as an external broker would.

### 5.2 Script format

Each immutable script is identified by its canonical hash and contains:

| Field | Meaning |
| --- | --- |
| `scenarioCode` / `scenarioId` | required catalog identity and run-specific identity |
| `virtualStartUtc` | deterministic observation time |
| `initialAccount` / `initialPositions` | broker facts, not Trading Core rows |
| `steps` | ordered match/action rules |
| `terminalAssertions` | broker-only facts expected after the scenario |

A step matches `operation`, optional `clientOrderId`, and zero-based call ordinal. Supported broker
operations correspond exactly to the existing port: submit entry, increase, close, cancel, get
orders, get positions, and get account.

Supported actions are deliberately finite:

```text
ReturnEvidence
RecordThenReturn
ThrowWithoutEffect
RecordThenTimeout
DelayVisibilityUntilBarrier
ReturnDuplicateEvidence
ReturnOutOfOrderEvidence
EnterOutageUntilBarrier
```

Evidence values use integer quantities, invariant decimal strings inside the broker fixture, and
explicit UTC timestamps. Unknown action, unmatched operation, excess call, or reused client order ID
with a different request hash fails the scenario rather than returning a convenient default.

### 5.3 Deterministic barriers

The driver advances named barriers such as `submission-recorded`, `partial-fill-visible`,
`terminal-fill-visible`, and `outage-cleared`. Emulator delays and failures wait on barriers rather
than wall-clock sleeps. A barrier transition is durable and idempotent. This makes timeout,
reordering, restart, and recovery scenarios reproducible on slow hardware.

## 6. Controlled time and Market Data evidence

The acceptance host binds a controlled `TimeProvider` to the same shared workers that production
binds to system time. The driver may only move time forward. Every advance is durably journaled with
the scenario and causation identity; moving backward or changing time after a financial effect with
an incompatible value fails the scenario.

The driver first requests a bounded historical completed-bar range from Market Data and records its
evidence identity, revision, hash, calendar, adjustment, and last-bar cutoff. It then constructs an
isolated position fixture whose artifact and initial watermark are consistent with that evidence.
The position is created only through an acceptance bootstrap operation in the acceptance host before
command acceptance opens. That operation:

- is absent from the production host and image;
- accepts only the driver role;
- permits only `acceptance-*` identities and the synthetic account generation;
- validates artifacts and Market Data evidence through the same shared compatibility policies;
- is permanently disabled after the scenario enters `Running`;
- records the fixture hash in the manifest.

Production Market Data remains read-only. No bar, evidence revision, canonical production position,
or production watermark is created or edited.

## 7. Scenario lifecycle

The driver processes the catalog sequentially:

```text
Prepare scenario identity, evidence, script, and expected hashes
  -> create fresh Trading Core and emulator PVCs/objects
  -> wait for both standard readiness contracts
  -> load and seal broker plan
  -> bootstrap isolated financial state while acceptance is fenced
  -> start scenario and open only its accepted command path
  -> drive commands, barriers, time, and scoped Pod loss
  -> wait on durable state predicates, never arbitrary long sleeps
  -> query final Trading Core state and emulator journal
  -> compute scenario result and append it to the manifest volume
  -> stop objects, retain PVCs until the result hash is durable
  -> delete exact scenario objects and continue
```

If the driver restarts, it reads the append-only run journal and resumes the same operation ID. A
partially executed scenario is either resumed from its durable predicates or marked failed; it is
never silently reset and counted as a fresh pass.

### 7.1 Pod-loss mechanics

The driver resolves the exact Pod UID and verifies its namespace and labels before deletion. Its
Role cannot delete production namespace resources. The Deployment recreates the Pod against the
same scenario PVC. For broker-survival cases only Trading Core is deleted; for broker-outage cases
the emulator remains present but returns the scripted outage. Infrastructure-node loss is outside
this single-node acceptance claim.

### 7.2 Assertions

Assertions observe stable business identities and hashes, not log text or timing luck:

- one client order ID and one durable financial effect;
- exact command status and reconciliation disposition;
- canonical position/trade/fill quantities;
- monotonic position watermark and policy-state hash;
- ordered broker call/effect journal;
- zero unexpected broker operations;
- active owner, generation, command fence, divergence, inbox identity, activity-journal integrity,
  and enabled-consumer lag;
- database integrity and resource objectives.

Logs support diagnosis but cannot turn a failed state assertion into a pass.

## 8. Run controller and manifest flow

The driver writes scenario fragments atomically to the manifest PVC. When the catalog is complete it:

1. validates required scenario coverage and shared assembly hashes;
2. derives the verdict using the A0 contract;
3. emits canonical redacted JSON and computes `manifestId`;
4. seals the run journal and refuses further scenario mutation;
5. exposes the artifact to the existing deployment workflow for copy-out;
6. waits for an external hash acknowledgement before namespace cleanup is permitted.

The supported implementation entry remains `scripts/deploy-k3s.sh` with a future explicit
acceptance scope. No second root deployment script or Compose variant is introduced. Cleanup may
delete only the exact generated namespace after validating its run label, sealed manifest ID, and
copy-out acknowledgement. Failed runs retain the namespace by default for bounded diagnosis and
require an explicit exact-run cleanup action.

## 9. Resource-load scenario

The load case uses the same production Trading Core limits and a bounded fixture representing the
approved maximum concurrent position/command/reconciliation workload. It records integer samples
for CPU millicores, working-set bytes, SQLite busy milliseconds, command/query latency milliseconds,
queue age, activity-journal size/consumer lag, reconciliation interval, restart count, and node swap
pressure.

The driver does not increase workload limits, start concurrent ML/optimization batches, or infer
high availability. Any OOM, sustained swap, missed reconciliation objective, database lock timeout,
or unexpected restart fails the configured envelope and returns `resource-objective-failed`.

## 10. Failure and cleanup rules

The run stops and seals a failed manifest when:

- a production Secret/volume reference or public egress is detected;
- a shared runtime assembly hash differs;
- the Market Data role reaches a non-evidence operation;
- a scenario attempts an unspecified broker call or time reversal;
- state leaks between scenario identities;
- a Pod/PVC resolves outside the exact run namespace;
- a required durable predicate cannot converge within its contract deadline;
- the manifest cannot be copied and hash-acknowledged outside the namespace.

The runner never repairs production state, broadens production broker egress, or invokes Alpaca/LS.
Namespace deletion is cleanup of isolated generated objects, not part of the pass verdict.

## 11. A1/A2 design exit criteria

A1/A2 are ready for review when:

- production and acceptance hosts share byte-identical financial runtime assemblies;
- scripted behavior and time controls cannot enter the production image;
- broker and Trading Core state survive independently;
- every allowed network edge and certificate role is explicit;
- the driver has no permission outside the generated namespace;
- scenarios are isolated without reset endpoints or shared mutable state;
- timeouts and ordering use durable barriers instead of timing assumptions;
- real Market Data evidence is read-only and production financial state is untouched;
- resource usage is bounded for the low-power single-node server;
- manifest copy-out is proven before exact namespace cleanup;
- A3–A5 can consume scenario and ownership evidence without redefining it.

Until this design is accepted, no acceptance host, emulator, driver, Kubernetes object, or runtime
port extraction is authorized.

## 12. Future implementation packages

The future work is grouped around one integration point rather than promoted sequentially:

```text
A1.1 Split BrokerPorts and the production-only Alpaca adapter
A1.2 Extract byte-identical shared Runtime and inject broker factory/time provider
A1.3 Add acceptance host, scripted broker client, and durable Broker Emulator
A2.1 Add generated namespace, RBAC, mTLS roles, default-deny policies, quotas, and PVC templates
A2.2 Add driver, scenario compiler, durable barriers, assertions, and manifest writer
                         |
                         v
A2.3 Integrate one acceptance image set and static manifest/object inspection
                         |
                         v
Later A6/A7 only: one full build/test gate and one isolated K3s acceptance batch
```

A1.1–A2.2 may be implemented against the frozen A0 contracts but are not deployed or counted as
separate milestones. Focused compiler feedback may be used during future implementation; full
verification and K3s execution wait for the integrated candidate as required by the MSA blueprint.
