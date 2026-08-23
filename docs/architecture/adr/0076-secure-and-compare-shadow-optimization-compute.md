# 0076 — Secure and compare shadow optimization computation

- Status: Accepted for K3s shadow activation
- Date: 2026-08-23
- Baseline: fbca0e9

## Context

ADR 0075 produced a complete Optimization Worker calculation but intentionally left executable
leases disabled. The cluster channel was authenticated HTTP, which exposed immutable strategy and
prepared market data to any node-level observer. The service also lacked authoritative-versus-Worker
result comparison, so a successful result submission did not prove execution parity.

During comparison work another historical defect became visible: background Stage 2 fallback used
the database Job ID as a random seed while synchronous execution used a different candidate path.
Identical requests could rank different candidates before any service boundary was involved.

## Decision

Expose a second, cluster-only Kestrel HTTPS endpoint on port 5443 while preserving the public API
HTTP endpoint on port 5239. Executable Worker requests require all of:

- TLS with server-name validation against the internal CA;
- a client certificate chaining to that CA with Client Authentication EKU;
- exact client common name `stocktrader-optimization-worker`;
- the existing independent 32-character-or-longer shared secret and Pod-derived Worker ID.

Server and client leaf certificates live in separate generation-named Kubernetes Secrets. An active
generation ConfigMap selects them, while older generations remain available for rollback. The Worker mounts only its
client key and CA bundle; it does not receive the server private key, database, or Kubernetes token.
The API mounts the server key and only the client CA. An egress NetworkPolicy limits the Worker to
cluster DNS and the API TLS port. The rotation script creates short-lived leaves without writing
private material to the repository and requires a coordinated API/Worker rollout.

For fresh, unrestricted jobs, Strategy Research publishes a comparable request whose ranking and
result-count settings match the background job. Both adapters now use one stable Stage 2 candidate
pool independent of Job ID. Limited, resumed, paused, or cancelled jobs are not compared to a full
remote run.

The authoritative writer remains the in-process executor. It stores a normalized audit snapshot on
the lease record only. Worker results are normalized by rank and parameter JSON, execution duration
is excluded, and exact IS/OOS metrics and periods are hashed. Match, mismatch, and awaiting states
are observable through authenticated Worker status, public API health, and structured logs. A
shadow recording or comparison failure cannot change the canonical in-process job outcome.

## Persistence and rollback

Migration `AddOptimizationShadowComparisons` adds only audit columns to the Strategy Research-owned
lease table. No Worker gains database access and no second writer touches `OptimizationResults`.
Pre-migration SQLite backup remains mandatory through `scripts/deploy-k3s.sh`.

Rollback sets `OptimizationWorkerTransport__LeaseTransportEnabled=false` or scales the Worker to
zero. Pending leases expire; canonical optimization jobs and results continue in-process. Audit rows
are retained. Certificate secrets can be rotated independently, but CA replacement requires both
workloads to roll together. A failed rotation rolls back by selecting the preserved prior generation
and redeploying both workloads; it does not depend on a mutable Secret or ReplicaSet snapshot.

## Historical-result correction

Stage 2 fallback now follows stable generated-combination order after preferred neighbors. The old
Job-ID shuffle and incomplete synchronous fine-search budget were execution-adapter artifacts, not
market behavior. Existing stored results are retained; reruns use the corrected deterministic path.
The named characterization test and impact are recorded in
`docs/release-notes/2026-08-23-deterministic-stage-two-optimization.md`.

## Remaining cutover gates

Shadow activation is not remote-authoritative cutover. Required evidence still includes multiple
matching real-Pod jobs across timeframes and strategy features, deliberate mismatch alert proof,
lease expiry/reclaim and Pod crash tests, capacity and latency measurements, certificate-rotation
drill, and rollback rehearsal. Only after those gates pass may the in-process calculation be removed.
