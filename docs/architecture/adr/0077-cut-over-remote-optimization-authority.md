# 0077 — Cut over optimization computation to the remote Worker

- Status: Accepted
- Date: 2026-08-23
- Baseline: b636cb7

## Context

ADR 0076 proved that the independent F# Worker could evaluate one real prepared-data optimization
job with exactly the same normalized result as the in-process executor. It did not complete the
service extraction: Strategy Research still performed every canonical calculation, the Worker was
labelled `shadow`, and cancellation, Pod loss, lease expiry, bounded multi-Pod load, and operational
rollback had not been exercised as one service lifecycle.

Calling that slice a completed MSA service created a misleading boundary. The completion unit is the
whole Optimization Worker: exclusive remote candidate evaluation, one canonical writer, durable
recovery, cancellation, observability, independent scaling, and a rehearsed return to in-process
execution.

## Decision

`OptimizationWorkerTransport.Mode=Remote` selects `RemoteOptimizationJobExecutor`. Strategy
Research prepares the immutable, versioned strategy and market-data input, publishes a canonical
lease, and polls for an accepted result. It does not instantiate or invoke the in-process candidate
evaluator in this mode. The F# Worker is the only candidate-computation process.

The Worker remains stateless and receives no database volume, Kubernetes API token, provider
credential, or canonical write credential. It claims work through the mTLS and shared-secret control
plane. Two replicas and bounded API-side preparation concurrency provide horizontal capacity without
two database writers.

Strategy Research remains the sole owner of `OptimizationJobs`, `OptimizationResults`, and durable
leases. After contract, input-hash, lease-generation, cancellation-generation, result-hash, rank,
and payload validation, it replaces the job's canonical result set in one SQLite transaction and
records `CanonicalCommittedAt` plus the normalized result hash. Retrying after a process failure
returns `AlreadyCommitted`; it cannot insert a second accepted result set.

User pause or cancellation changes the job state and invalidates the active canonical lease
generation. A Worker heartbeat observes the stop and cancels computation. A Worker or Pod that
disappears leaves no partial canonical result; after the lease expires another Pod claims a higher
generation, and a stale generation cannot heartbeat or submit. An API restart requeues interrupted
jobs and reuses the durable lease or the already committed result.

Remote execution accepts a deterministic tested-combination limit, but not a wall-clock duration
limit. The public metadata contract reports that capability so clients do not invent a second list
of execution features.

## Persistence, deployment, and rollback

The EF migration `CompleteRemoteOptimizationAuthority` adds authority and canonical-commit audit
columns. Existing rows default to `Shadow`; the Worker still has no database access.

The supported K3s deployment path injects the same `Remote` or `Shadow` mode into the API and Worker,
uses generation-scoped TLS Secrets, and deploys two Worker replicas with rolling availability and
resource limits. Readiness fails when the configured control plane is disconnected; liveness remains
independent so Kubernetes does not turn an API outage into a restart loop.

Rollback is an explicit deployment of both workloads with:

```bash
STOCKTRADER_OPTIMIZATION_MODE=Shadow \
STOCKTRADER_OPTIMIZATION_LEASE_TRANSPORT_ENABLED=true \
./scripts/deploy-k3s.sh <source-tag>
```

In Shadow mode the in-process executor is authoritative and Worker output is comparison-only. If
the transport itself is unsafe, set lease transport to `false`; the in-process path continues and no
new Worker lease is claimable. Canonical rows already committed before rollback remain valid and no
financial or broker state is involved.

## Completion evidence

The final service-level verification batch passed on the production K3s deployment at `b636cb7`:

- the backend build and all 1,000 backend tests passed; Worker build, two compute tests, generated API
  check, 75 desktop tests, desktop build, migration model check, architecture tests, and Linux script
  syntax checks also passed;
- two simultaneous four-candidate TQQQ daily jobs were claimed by different Worker Pods and produced
  the same ordered financial metrics and normalized canonical hash as the prior in-process result;
- a live 10,000-candidate cancellation invalidated its lease and produced Worker cancellation
  telemetry without a canonical commit;
- deleting the active Worker Pod caused the expired generation-1 lease to be reclaimed by the other
  Pod as generation 2; all 10,000 candidates then committed once;
- restarting the API during another active 10,000-candidate job reused the same durable lease and
  committed once; an independent coordinator retry returned the existing canonical commit;
- a full `Shadow` deployment completed an in-process four-candidate job, produced comparison-only
  Worker state, and was then returned to `Remote`;
- internal CA generation `otls-b636cb7` was deployed, the preserved generation
  `20260823114459` was successfully restored, and production was returned to `otls-b636cb7`;
- the final API and two Worker Pods were Ready on source-tagged images with zero restarts. Both
  Workers reported control connected, contract version 3, and no active lease. Idle samples were
  39–41 millicores and 33–35 MiB per Worker. Expected connection-refused retries occurred only while
  the API Pod was intentionally unavailable during the joint certificate rollout; readiness and
  control connectivity recovered without intervention and the final API log contained no warning or
  error.

This completes the Optimization Worker as the first independently deployable MSA service. It does
not make the single-node K3s cluster highly available and does not complete the wider MSA program.
The next extraction boundary is Market Data.
