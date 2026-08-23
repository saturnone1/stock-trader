# 0079 — Extract ML Training and immutable model publication

- Status: Proposed, pending complete service cutover verification
- Date: 2026-08-24
- Baseline: `e419b5d`

## Context

The API currently schedules and executes both ML.NET trainers, reads causal signal outcomes from the
application database, requests regime bars through the selected Market Data adapter, writes mutable
model ZIP/manifest pairs under the API data volume, and loads those same files for inference. That
mixes input preparation, expensive batch computation, publication authority, consumer caching, and
live fallback in one process.

The production baseline has no loaded model and zero causal signal/outcome samples. Regime training
can use the accepted Market Data service, but signal scoring must remain deliberately unavailable
until at least the configured minimum of causal, chronologically splittable win/loss samples exists.
An extraction must not invent labels or make an incomplete model executable merely to demonstrate a
successful Pod.

Shared model directories are not a service boundary. A second Pod mounting `/data/ml_models` would
create two mutable writers with no durable publication identity, and a Worker reading
`stocktrader.db` would violate service-owned database boundaries.

## Decision

Create an independent F# ML Training service with its own OCI image, Kubernetes Deployment,
ServiceAccount, NetworkPolicy, mTLS identity, shared-secret workload credential, probes, metrics,
durable SQLite job store, and service-owned immutable artifact directory. It mounts neither the API
database nor the API model cache and receives no broker/provider/account credentials.

The Control API remains the input authority:

- it obtains completed, evidence-bound regime bars through Market Data and calculates the existing
  causal regime feature schema;
- it reads causal signal features and realized labels through the existing purpose-specific store;
- it submits one versioned immutable training request carrying feature-schema versions, trainer
  version, observation cutoff, Market Data evidence identity, ordered samples, settings, and a
  recomputable input hash.

The ML Training service owns durable job identity, computation, result history, model bytes,
manifest identity, and publication revision. Duplicate delivery of the same job/input returns the
same result. Reusing a job identity with different input fails closed. Model artifacts bind model
kind, trainer/feature versions, training cutoff, sample count, metrics, complete regime label map
where applicable, model SHA-256, and an artifact ID recomputed from that manifest.

Training computation lives once in an independent C# `StockTrader.MlTrainingCompute` library because
ML.NET is a C#-centric library and the existing algorithms must not be translated or duplicated.
The concise F# service owns only transport, durable orchestration, and operations. Local
compatibility and the remote service call the same compute facade.

The API supports `Local`, `Shadow`, and `Remote` transport modes:

- `Local`: the shared compute facade trains and publishes to the existing validated consumer cache;
- `Shadow`: Local remains authoritative while the service receives the exact immutable input. The
  comparison binds status, manifests, metrics, labels, and deterministic prediction probes; the
  remote artifact is not loaded by live inference;
- `Remote`: only the service trains and publishes. The API downloads an immutable artifact, verifies
  every hash/version/label invariant, and atomically refreshes a read-only consumer cache. The cache
  is not publication authority and is reconciled to the service publication revision at startup.

Partial success is explicit. Regime and signal artifacts have independent statuses. Insufficient
causal signal samples produce no signal artifact and do not invalidate a valid regime artifact.
Inference continues its existing fail-closed deterministic fallback for any absent, stale,
incompatible, partial, or tampered artifact.

## Failure and rollback policy

Training is not a live-trading dependency. Service loss makes new manual/automatic training return a
typed unavailable result, while the last verified consumer artifact or deterministic fallback
continues. The API must not silently train locally in `Remote` mode.

Rollback switches scheduling and manual training to `Local` only after backing up the consumer cache.
It never makes the service database or artifact directory writable by the API. A Remote artifact may
be imported into the Local cache only through the same manifest/hash verifier. Cancelling, retrying,
restarting, or replaying a job cannot publish two artifacts for one input.

## Deployment and working-set budget

The first deployment uses one replica because SQLite owns the durable queue and publication
revision. A multi-replica trainer requires a lease-capable database or an explicit single-active
leader decision. The supported deployment path remains `scripts/deploy-k3s.sh`, with independent
backup/restore and generation-scoped TLS rotation scripts.

The F# host starts below 200 nonblank lines per orchestration file. Training formulas, feature order,
dataset split, cluster meaning, metrics, and artifact construction live in the shared compute or
contract assemblies with zero source copies in the service host.

## Acceptance evidence required

This ADR becomes Accepted only when one service-level batch proves:

- independent image, Pod, identity, volume, secrets, mTLS, probes, metrics, logs, and resource limits;
- the Pod mounts neither application/Market Data databases nor API model directories;
- contract/hash rejection for mutation, unsupported versions, non-causal order, and incomplete
  regime label maps;
- Local and service computation use one implementation and pass the same chronological/causal
  conformance corpus;
- Shadow parity on the production regime input and explicit insufficient-signal parity;
- Remote exclusive training with no in-process trainer and immutable verified consumer promotion;
- duplicate, concurrent, cancellation, Pod loss, API loss, timeout, corrupt artifact, stale result,
  and no-fallback behavior;
- load, resource, backup/restore, TLS rotation/rollback, and `Remote`/`Local` rollback;
- API model status and inference restart load only the accepted artifact revision;
- Market Data evidence and feature cutoffs prove that no future bar or post-entry signal feature
  enters training.

Until these gates pass, ML Training is incomplete and Reporting/Notifications or Trading Core
extraction must not begin.
