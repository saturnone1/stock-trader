# 0075 — Extract prepared-data optimization computation

- Status: Accepted behind disabled shadow transport
- Date: 2026-08-23
- Baseline: fec4108

## Context

The durable Worker lease proved ownership and recovery but returned only an identity receipt. It did
not demonstrate that an independently built Pod could execute the same candidate search, prepared
data slicing, strategy detection, fills, costs, portfolio accounting, ranking, and out-of-sample
evaluation as Strategy Research. Reimplementing these rules in F# would create a second strategy
engine and make historical results depend on which process executed the job.

## Decision

Add `StockTrader.OptimizationCompute`, a provider- and persistence-free C# computation assembly. It
composes the existing deterministic optimization and backtest sources with `StockTrader.Engine` and
accepts only a versioned `OptimizationWorkLease`. The F# Optimization Worker owns process lifecycle,
HTTP control-plane communication, lease heartbeats, cancellation, and result submission; it calls
the C# computation facade rather than duplicating trading rules.

The prepared input now binds PatternSettings plus market time zone and warmup conditions into its
canonical identities. The computation maps immutable bars, aligned indicators, regimes, risk, and
market-data evidence back to the shared execution inputs, then runs the complete Stage 1, Stage 2,
ranking, and OOS pipeline. The result contract contains ranked parameters and IS/OOS metrics but is
stored only on the lease audit record. It cannot write `OptimizationResults`, trades, orders, or any
financial state.

Long computations maintain their lease through periodic authenticated heartbeats. A rejected
heartbeat or changed cancellation generation cancels local computation. Result acceptance validates
purpose, input identity, bounds, result hash, and parameter JSON before completing the lease.

## Transitional source ownership

The new assembly links the current deterministic source files instead of copying or translating
their logic. This keeps one repository source of truth and produces an independently deployable
Worker image, but two assemblies temporarily compile some identical types. They must not be loaded
as competing implementations in one application composition root. Compute conformance tests live in
their own project, and the next engine-extraction stages will move these sources into package-free
shared projects so linking can be removed.

## Safety and activation

`LeaseTransportEnabled` remains false in Compose and K3s. The in-process optimizer remains the only
authoritative result writer. This decision proves a complete computation boundary locally; it does
not authorize plaintext prepared-data transport or a production cutover.

Before activation, internal TLS/workload identity, real-Pod lifecycle tests, canonical shadow-result
comparison, load/crash tests, observability, and a rollback drill remain mandatory.

## Verification

The compute project builds independently, the F# Worker builds against it, and dedicated tests run a
full search from only contract data and reject a non-compute lease. Existing backend tests continue
to characterize the monolith semantics. The container build verifies that the independent image can
restore and publish the complete computation graph without referencing `StockTrader.csproj`.
