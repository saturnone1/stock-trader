# ADR 0009: Deterministic optimization job policy

## Status

Accepted

## Context

The background optimization worker previously owned out-of-sample date splitting, random search
budgets, restart chunk calculations, duration limits, and direct system-clock reads. Synchronous
and background optimization also duplicated slippage, commission, and cost-model constants. A
worker restart or a future edit to only one path could therefore change candidate ordering or make
otherwise identical candidates incomparable.

## Decision

`Application/Optimization/OptimizationJobExecutionPolicy` is the deterministic owner of period
splitting, coarse/fine search budgets, evenly distributed candidate selection, duration boundaries, and restart
chunk positions. Its inputs contain all time and random-seed observations; it does not read system
time, persistence, or services.

`OptimizationBacktestAssumptions` owns the execution costs used only for candidate comparison.
Synchronous and background optimization consume those same assumptions. User-requested ordinary
backtests continue to use their explicit request values.

Workers receive `TimeProvider` and use it for timestamps and polling delays. They retain I/O,
repository status checks, scoped dependency resolution, logging, and cancellation coordination.

## Consequences

- The same request generates the same stage-one candidates across modes, jobs, and restarts.
- OOS boundaries and 60/40 search budgets can be tested without a host or database.
- Optimization candidates are ranked under one cost model regardless of execution mode.
- The worker remains a coordinator and is capped at 500 lines while further use-case extraction
  proceeds incrementally.
