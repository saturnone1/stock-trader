# ADR 0006: Persist strategies through one management use case

## Status

Accepted

## Context

The custom-strategy HTTP endpoints and optimization auto-tuner both wrote `CustomPatterns` through
EF Core. They duplicated name, timestamp, version, and field-copy behavior. More importantly, the
backtest-result and optimizer promotion paths could persist invalid parameters without compiling the
resulting strategy first.

## Decision

All operator and optimizer mutations of a stored custom strategy pass through
`CustomPatternManagementService` in the Application layer. It compiles a candidate before writing,
owns server identity and timestamps through `TimeProvider`, normalizes names, stamps the current
document version, and reports typed success, invalid, conflict, or not-found outcomes.

Persistence is supplied by the purpose-specific `ICustomPatternStore` port. Its EF implementation is
the only owner of query tracking and database mutation. API adapters translate HTTP contracts and
status codes; background workers translate job outcomes. Neither owns strategy validation or writes
`CustomPatterns` directly.

## Consequences

- Manual edits, backtest parameter application, and automatic optimization promotion share one
  validation and persistence boundary.
- Invalid optimized candidates leave the strategy and applied-result count unchanged.
- Application tests can exercise management semantics with an in-memory port and deterministic time.
- Database uniqueness races remain an infrastructure concern and require a case-insensitive database
  constraint before concurrent writers are considered fully protected.
