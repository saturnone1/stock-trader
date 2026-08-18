# ADR 0027: Isolate live signal evaluation state

## Status

Accepted

## Context

`SignalService` directly queried completed trades, open positions, executed recommendations, and
ticker sectors through EF Core. The live cooldown policy also accepted the persisted `TradeRecord`
entity. Changing database shape or testing recommendation policy therefore required understanding
storage details, while multiple queries and their filtering rules had no named application owner.

## Decision

- `ILiveSignalEvaluationStore` is the purpose-specific application read port for one signal
  evaluation cycle.
- `LiveSignalEvaluationSnapshot` exposes only ordered completed-strategy trades, the total open
  position count, per-strategy entries executed since the supplied market-session boundary, and
  sector lookup by symbol.
- `LiveSignalEvaluationStore` owns all EF projections and returns detached, case-insensitive
  dictionaries.
- `StrategyCompletedTrade` is the persistence-independent input shared by cooldown, drawdown, and
  position-sizing decisions. The live policies never accept `TradeRecord`.
- The application service remains the owner of the clock and US market-date boundary. The store
  receives that boundary rather than reading system time.
- When no custom strategies are evaluated, completed-trade, open-position, and executed-entry
  reads retain the previous empty-state behavior; sector lookup remains available to built-ins.

## Consequences

Live recommendation rules can be tested without EF entities, storage queries can be optimized in
one adapter, and persistence schema changes no longer propagate into cooldown or sizing policy.
SQLite adapter tests lock ordering, market-session inclusion, case-insensitive identity, total-open
position semantics, and built-in sector lookup.
