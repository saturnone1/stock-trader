# 0074 — Establish durable Optimization Worker leases

- Status: Accepted for disabled shadow transport
- Date: 2026-08-23
- Baseline: baa21ba

## Context

The independent F# Pod can authenticate to Strategy Research, but the status probe proves neither
exclusive work ownership nor recovery after an API or Worker restart. Sending prepared OHLCV data
without a durable lease would allow concurrent workers, stale results, and lost cancellation state.
Letting the Worker read the application SQLite file would instead create a shared-database service
boundary and bypass the contract.

The current cluster-local channel is authenticated HTTP. ADR 0073 permits that only for the status
handshake and requires internal TLS or workload identity before executable lease payloads cross the
Pod boundary.

## Decision

Strategy Research exclusively owns a new OptimizationWorkerLeases table and exposes authenticated
pull-lease, heartbeat, and result endpoints. The Worker has no database volume, credentials, or
direct persistence access.

An in-process optimization prepares one immutable OptimizationEvaluationInput and idempotently
publishes a shadow-validation lease. Failure to publish is logged and cannot change the authoritative
in-process result. Conditional database updates provide:

- one Worker owner per lease generation;
- expiry and generation-incrementing recovery;
- heartbeat extension bound to Worker, lease, cancellation generation, and input hash;
- cancellation when the source job is paused or cancelled;
- exact duplicate submission acceptance after expiry;
- rejection of a different submission against an already completed lease;
- server-side recomputation of result hash and validation-receipt identities.

The only accepted payload is currently shadow-contract-validation-v1. It reports the exact strategy,
evidence, prepared-data, series, and bar identities. It is stored only on the lease record and never
enters OptimizationResults, auto-tuning, or a financial state transition.

LeaseTransportEnabled is a separate fail-closed switch. It defaults to false in application,
Compose, and K3s configuration. The API may expose the authenticated status endpoint while refusing
to publish or claim executable leases. Production activation is prohibited until an extraction ADR
selects and verifies internal TLS/workload identity.

## Persistence and recovery

The new table belongs to the existing Strategy Research owner because canonical jobs and accepted
optimization results remain there. This is not a Worker database and is never mounted into the
Worker Pod. EF migration AddOptimizationWorkerLeases creates unique job/purpose/input and submission
identities. The supported deployment script takes and checks a SQLite backup before applying it.

Scaling the Worker to zero or setting LeaseTransportEnabled=false stops new claims. Existing leases
expire without affecting the in-process optimizer. Deleting lease records is not part of rollback;
they are audit evidence and cascade only when their owning optimization job is deleted.

## Conformance evidence

Tests cover idempotent publication, concurrent single-owner claim, heartbeat ownership, expiry
re-leasing, stale-generation rejection, source-job cancellation, payload tampering, exact duplicate
submission, and changed-submission rejection. Shared policies reject unsupported versions,
identity/input mismatches, negative progress, cancellation generations, expiry, and hash changes.

This is not the Stage 2 compute cutover. The F# Worker validates contracts but does not yet run the
complete shared backtest simulation. The in-process executor remains authoritative.

## Agent working-set budget

The F# service is split into files of 66 physical lines or fewer: state 45, HTTP client 51, lease
processor 66, control loop 61, health 43, and CLI 56. The C# persistence adapter is split by lease
claim, heartbeat, and result acceptance; no orchestration source file exceeds 200 physical lines.
The Worker has three direct project dependencies and duplicates zero strategy, indicator, fill,
cost, portfolio, or contract policies.

## Next gate

Before LeaseTransportEnabled may become true in K3s:

1. select internal TLS/workload identity and define certificate rotation;
2. prove the API rejects plaintext lease traffic and untrusted identities;
3. exercise the full lease lifecycle across real Pods without enabling remote computation;
4. extract the remaining prepared-data simulation into the shared deterministic engine;
5. compare complete remote and in-process results under load, delay, duplicate, crash, and rollback.
