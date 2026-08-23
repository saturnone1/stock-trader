# 0070 — Establish the optimization-worker contract boundary

- Status: Accepted for Stage 1 implementation
- Date: 2026-08-23
- Baseline: `codex/research-flow-simplification` at `13aa948`

## Context

ADR 0069 selected a stateless optimization worker as the first process-extraction candidate. The
current scheduler, executor, market-data preparation, candidate evaluator, and SQLite checkpoint
store already have useful in-process seams, but the scheduler still depends on a concrete
background-service class. A job is identified mainly by an integer ID and mutable progress fields;
there is no transport-neutral statement of the exact strategy semantics and price evidence that
produced a result.

Starting a second process before those identities exist would allow a restarted or delayed worker
to submit a result for a replaced lease, a cancelled generation, changed strategy semantics, or
corrected market data. A shared database would hide rather than solve that ambiguity.

The service code also needs to stay small enough for a person or AI agent to understand without
loading the monolith. Reimplementing the trading engine in a terser but incompatible runtime would
reduce local line count while duplicating the most safety-critical semantics.

## Decision

Stage 1 begins with a process-neutral optimization execution port and versioned immutable contract
types. The production scheduler continues to use the existing in-process executor through
`IOptimizationWorkExecutor`; no network transport, second writer, schema migration, or additional
deployment is introduced by this decision.

The contract foundation contains:

- a storage-independent `StrategyExecutionArtifact` with the complete `StrategyDocument`, content
  hash, compiler schema, engine semantics, indicator/pattern catalog, calendar, and cost-model
  versions;
- an `OptimizationDataEvidenceSet` with provider, market, timeframe, adjustment, session,
  requested/observed range, bar count, completeness state, and deterministic OHLCV content hash for
  every symbol series;
- an `OptimizationEvaluationInput` binding the serialized request to the strategy and data evidence
  identities;
- lease, heartbeat, and result-submission contracts carrying lease generation, cancellation
  generation, input hash, expiry, and stable submission identity;
- a fail-closed result-acceptance policy for unsupported, stale, cancelled, mismatched, expired,
  mutated, and duplicate submissions;
- canonical JSON hashing that sorts object properties and preserves array order.

Extracted computation and orchestration services will use **F# by default**. The F# host will
reference the existing .NET contract and deterministic-engine assemblies. F# is selected for its
compact immutable/data-oriented code while retaining binary compatibility with the C# engine.
Strategy compilation, indicators, fills, costs, and portfolio transitions remain one C# source of
truth and must not be translated into F# service-specific logic. A later service that never evaluates
trading semantics, such as a reporting projection, may compare Go separately in its own ADR.

Content hashes are identities, not signatures. A later transport must authenticate its workload and
protect traffic independently.

The existing data providers do not yet prove that a prepared historical range is gap-free. Contract
generation therefore marks current evidence `Unverified`; it must not invent `Complete`. A remote
worker rollout cannot pass its release gate until the data owner can produce a stronger completeness
claim or the research result explicitly retains the degradation.

## Compatibility and conformance

The first contract version has no backward compatibility obligation because it has no deployed
remote consumer. Once a second process is introduced, producer and consumer must support a named
compatibility window and reject unknown versions.

Characterization tests require that:

1. persistence IDs do not affect strategy content identity;
2. a semantic strategy change changes the artifact identity;
3. one corrected OHLCV value changes both data evidence and evaluation input identity;
4. stale leases and cancellation generations cannot be accepted;
5. expired, mutated, and duplicate submissions have distinct outcomes;
6. the current scheduling loop depends only on the application execution port.

The broader preview/backtest/live conformance corpus remains mandatory before a remote worker is
enabled. This ADR does not weaken those gates.

## Deferred decisions

This decision deliberately does not select HTTP, gRPC, a message broker, a worker database, mTLS,
or an observability stack. Stage 0 measurements and a Stage 2 extraction ADR must select those using
the actual single-node K3s cost and recovery constraints.

It also does not move job lifecycle or accepted-result ownership. Strategy Research remains the sole
owner. An Optimization Worker will own only an expiring computation lease.

The contract-only F# shadow validator may be created after the contracts become an independent .NET
library. It may validate and report on a lease but cannot claim, evaluate, heartbeat, or submit work.
Before computation is enabled, the deterministic engine must also be an independent .NET library.
Referencing the ASP.NET web project from the worker is forbidden because it would preserve the
monolith under a second executable name.

## Rollback

The code rollback is to inject the concrete in-process executor into the scheduler and remove the
unused contract types. There is no data or deployment rollback because this increment creates no
new persistence and no second runtime.

## Consequences

The scheduler can later switch between an in-process adapter and a remote lease adapter without
absorbing transport or persistence rules. Results gain the identities needed for shadow comparison
and stale-result rejection. F# service shells can stay compact without forking engine semantics. The
immediate cost is contract maintenance and hashing work in tests; production data hashing is not
enabled until its performance baseline and storage policy are measured.
