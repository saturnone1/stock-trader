# ADR 0017: Isolate optimization result promotion

## Status

Accepted

## Context

Automatic and manual optimization-result application lived in a singleton background component.
That component opened dependency-injection scopes, loaded EF entities through the broad optimization
repository, decoded persisted JSON, ranked promotion candidates, updated strategies, recorded apply
metadata with read-modify-write, and recycled continuous jobs. Concurrent apply requests could lose
an applied-result count, and the promotion policy could only be tested with persistence entities.

## Decision

`OptimizationAutoTuneService` is a scoped application use case. It depends on the
storage-independent `IOptimizationAutoTuneStore` and `CustomPatternManagementService`.
`OptimizationPromotionPolicy` owns candidate eligibility, IS/OOS metric selection, ranking, and the
rolling continuous-request window using application records only.

The SQLite adapter owns request and parameter JSON compatibility, maps result identity and metrics,
increments apply counts with one database expression, and deletes prior results plus resets the same
job inside one transaction when continuous optimization is recycled. The hosted scheduler creates a
scope only when it invokes this use case; HTTP requests use their existing request scope.

## Consequences

- Promotion policy tests do not construct EF entities or infrastructure services.
- The application use case contains no EF, persistence models, JSON, broad repository, or service
  locator dependency.
- Concurrent successful applications cannot overwrite each other's applied-result increment.
- A continuous cycle exposes either its completed results or a fully reset pending job, never a
  partially recycled state.
- Persisted malformed candidate parameters fail closed without modifying the stored strategy.
