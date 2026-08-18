# ADR 0034: Isolate live-position execution persistence

## Status

Accepted

## Context

The live position coordinator depended on `ITradeRepository`, which also exposed trade queries,
positions, recommendations, and signals. Its execution methods accepted the EF-backed `TradeRecord`
entity. This made a safety-critical order-state transition depend on a broad persistence surface and
allowed application orchestration to know the storage entity used for realized trades.

## Decision

- `ILivePositionExecutionStore` is the application port for conditional claim, broker order evidence,
  claim release, and atomic fill commit.
- `PositionExecutionTrade` carries the realized-trade values required by a sell fill without exposing
  an EF entity to the coordinator.
- `LivePositionExecutionStore` creates an isolated `AppDbContext` for every operation. Fill updates,
  scaling counters, and the optional realized-trade insert share one SQLite transaction.
- `ITradeRepository` no longer exposes execution lifecycle methods. It remains a transitional read
  and general trade/signal repository while later use cases move to narrower ports.
- Architecture tests prevent the coordinator from regaining `ITradeRepository`, `TradeRecord`, or EF
  dependencies and cap the reduced broad repository at 350 lines.

## Consequences

The order coordinator can be tested entirely against a four-method persistence port, while SQLite
compare-and-set and transaction details remain in one adapter. A failed store transaction cannot
leave tracked execution entities in the caller's scoped context. Existing execution semantics,
restart recovery, partial-profit behavior, weighted scale-ins, and scaling counters remain locked by
the same database and coordinator goldens.
