# 0072 — Deploy the shadow Optimization Worker

- Status: Accepted for shadow deployment
- Date: 2026-08-23
- Baseline: `codex/msa-optimization-contracts` at `2e941bf`
- Extends: ADR 0069, ADR 0070, and ADR 0071

## Context

The F# Optimization Worker can validate version-2 immutable leases and references only independent
contract, protocol, and deterministic-engine projects. It is not yet a Kubernetes deployment and
cannot prove image isolation, health behavior, resource governance, workload identity, or rollout.
It also cannot compute or submit jobs, so enabling remote leasing now would be false progress.

The first deployment must not read the monolith SQLite file. Strategy Research remains the sole
owner and writer of optimization jobs and accepted results. A shared database would remove the
network boundary without creating service ownership.

## Decision

Publish `StockTrader.OptimizationWorker` as its own OCI image and Kubernetes `Deployment` in
explicit `shadow` mode. The host exposes only liveness, readiness, and Prometheus-text metrics. It
does not expose a job API, receive a data volume, claim work, heartbeat, or submit results.

The Pod has a dedicated ServiceAccount with token automount disabled, no secrets, no persistent
volume, non-root execution, a read-only root filesystem, dropped Linux capabilities, runtime-default
seccomp, and explicit CPU/memory requests and limits. No Kubernetes `Service` is created because
the eventual worker communication direction is outbound pull; health probes address the Pod
directly.

`scripts/deploy-k3s.sh` remains the only K3s deployment entry point and gains the independent
`optimization-worker` scope. The existing Docker Compose file gains the same image for supported
local container verification; no second compose variant is introduced.

## Future transport boundary

The next Stage 2 ADR increment will implement an authenticated bounded pull-lease API owned by
Strategy Research. The worker will never query the Strategy Research database. Lease creation,
cancellation generation, expiry, duplicate detection, and accepted-result persistence terminate at
the owner. The remote adapter will be shadowed against the in-process executor before scheduling is
cut over.

This decision deliberately does not claim that optimization computation has been extracted. It
establishes and deploys the independent workload shell needed to measure its idle cost and verify
K3s operational controls without changing job behavior.

## Failure modes and rollback

- A failed shadow Pod cannot stop in-process optimization because it owns no leases.
- A health or image regression is rolled back with Kubernetes rollout undo or by deploying the
  previous immutable image tag.
- Immediate rollback is `kubectl scale deployment/stocktrader-optimization-worker --replicas=0`;
  no database or financial reconciliation is required.
- The API and desktop images do not need rebuilding or restarting when only the worker scope rolls
  forward or back.

## Verification gate

Before this deployment may leave shadow mode, evidence must show:

1. independent image build and K3s rollout succeed;
2. liveness, readiness, resource limits, ServiceAccount, and read-only filesystem are effective;
3. contract/version mismatch fails closed;
4. idle and validation CPU/memory metrics are recorded;
5. remote lease authentication, expiry, cancellation, idempotent submission, and rollback tests
   pass without worker database access;
6. result conformance matches the in-process path.

## Agent working-set budget

The service owns 77 nonblank F# lines across a 51-line CLI/validation host and a 26-line health
host. Its project has three direct project references and one shared ASP.NET runtime framework; it
does not reference the monolith. The service duplicates zero contract, catalog, or trading-policy
lines. Deployment behavior is contained in one image file and one Kubernetes manifest.
