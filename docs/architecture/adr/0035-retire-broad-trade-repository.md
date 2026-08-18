# ADR 0035: Retire the broad trade repository

## Status

Accepted

## Context

`ITradeRepository` combined trade history, open positions, recommendations, pattern signals, and
several unused mutation methods. API endpoints also launched parallel reads against one scoped
`AppDbContext`, which Entity Framework does not permit. Mutable open-position entities were cached
and could expose an in-progress change to another request before persistence completed.

## Decision

- Replace the broad contract with four application ports: `ITradeHistoryStore`,
  `IOpenPositionStore`, `ITradeRecommendationStore`, and `IPatternSignalStore`.
- Each SQLite adapter creates an isolated `AppDbContext` for every operation through
  `IDbContextFactory<AppDbContext>`.
- Do not cache open-position entities. Recommendation and signal read caches remain bounded and are
  invalidated by their owning adapter.
- Remove unused single-entity signal and recommendation update operations instead of carrying dead
  persistence capabilities forward.
- Keep live entry and position state transitions in their existing atomic execution stores.

## Consequences

Endpoints may safely run independent reads in parallel, and each worker or service declares only the
trading data it consumes. The deleted catch-all repository can no longer accumulate unrelated order
or research behavior. Existing position, signal idempotency, recommendation idempotency, and live
execution goldens continue to protect persisted behavior.
