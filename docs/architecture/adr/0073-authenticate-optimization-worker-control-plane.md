# 0073 — Authenticate the Optimization Worker control plane

- Status: Accepted for shadow handshake
- Date: 2026-08-23
- Baseline: `codex/msa-optimization-contracts` at `b4ff763`
- Extends: ADR 0070 and ADR 0072

## Context

The shadow Worker runs as an independent Pod but has no authenticated relationship with Strategy
Research. Reusing the desktop cookie would mix browser sessions, CSRF behavior, and human identity
with workload identity. Giving the Worker database access or a Kubernetes API token would violate
least privilege and job ownership.

The current cluster is one controlled K3s node. Introducing a message broker or full service mesh
only for a non-computing handshake would add more state and recovery surface than the first boundary
can justify.

## Decision

Add a dedicated ASP.NET authentication scheme and authorization policy for the Optimization Worker.
The scheme requires a non-empty Worker ID and an independent secret of at least 32 characters,
compares the secret in fixed time, and emits only an `optimization-worker` service claim. It does
not accept the desktop cookie. Internal endpoints are excluded from public OpenAPI and have a
separate rate limit.

The transport defaults to disabled in application configuration. K3s explicitly enables only
`Shadow` mode and injects the secret from `stocktrader-optimization-worker-auth` into the API and
Worker containers. The secret is not stored in Git, image layers, contract payloads, logs, or a
volume. The Pod name is the initial Worker ID.

The F# Worker performs an outbound status probe over the cluster-local API Service and publishes
connection attempts, successes, and current connection state through its existing health/metrics
host. A missing URL, ID, or secret leaves the probe unconfigured. Shadow readiness remains process
readiness; loss of the control API cannot change optimization behavior because remote leasing is
not enabled.

Cluster-local HTTP is accepted only for this single-node shadow handshake. Before traffic crosses a
node boundary or carries executable leases, the extraction gate must select internal TLS or a
workload-identity mechanism and test credential rotation.

## Ownership and failure behavior

- Strategy Research remains the only optimization database reader/writer.
- The Worker receives no database, user cookie, Alpaca credential, or Kubernetes token.
- Disabled transport, missing credentials, wrong credentials, and wrong service claims fail closed.
- An API or probe outage changes only connection telemetry in shadow mode.
- The current one-secret scheme does not claim zero-downtime rotation; remote computation cannot be
  enabled until overlapping generations or an equivalent atomic rotation procedure is proven.

## Next gate

The authenticated path may add lease, heartbeat, and result endpoints only after their application
coordinator and durable owner-side idempotency store exist. Enabling `Remote` mode additionally
requires disabling the in-process claimant, stale-lease recovery, cancellation generation tests,
and shadow result conformance.

## Rollback

Disable `OptimizationWorkerTransport__Enabled` or scale the Worker Deployment to zero. The existing
in-process optimizer is unaffected, and no database or result reconciliation is necessary at this
stage.

## Agent working-set budget

The F# service now owns 157 nonblank lines: 51 for CLI/lease validation, 36 for health and metrics,
and 70 for the control-plane probe and its concurrency-safe state. No file exceeds 100 physical
lines. Header names remain in the shared contract assembly, and the F# host duplicates zero
authentication constants or trading policies.
