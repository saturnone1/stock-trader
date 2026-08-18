# ADR 0010: Share optimization candidate evaluation

## Status

Accepted

## Context

Synchronous optimization and the persistent background executor used the same prepared market data
but independently cloned strategy variants, selected timeframe-specific data, invoked the prepared
simulation engine, handled candidate failures, and converted fractional backtest metrics into the
percent units stored by optimization results. A change to only one copy could alter rankings or OOS
values even when the search plan and execution-cost assumptions remained identical.

The background executor also depended on the concrete `BacktestService` simulation method. That made
the worker responsible for trading calculations instead of only job lifecycle, chunks, persistence,
and cancellation.

## Decision

`Application/Optimization/IOptimizationCandidateEvaluator` is the purpose-specific application port
for evaluating one or more parameter snapshots against already prepared data. Its context contains
the strategy request, explicit timeframe maps, regimes, and risk inputs without exposing the
backtest service's internal parameter type.

`Services/Backtest/OptimizationCandidateEvaluator` implements the port by cloning the strategy
document, applying overrides, selecting the candidate timeframe, and calling
`BacktestPreparedSimulationRunner`. Both synchronous and background optimization use this same
implementation for IS, fine-search, and OOS evaluation.

`OptimizationResultProjection` is the pure owner of backtest-to-optimization metric units. Database
entities remain outside the application port; the worker copies the projected values at its
persistence boundary.

## Consequences

- Candidate execution semantics and metric conversion cannot drift between optimization modes.
- The background worker no longer clones strategies or directly invokes the simulation engine.
- The evaluator can be tested or replaced without exposing EF repositories to the application port.
- Market-data preparation and job persistence remain separate incremental seams for later extraction.
