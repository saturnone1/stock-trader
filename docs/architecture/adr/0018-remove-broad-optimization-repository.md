# ADR 0018: Remove the broad optimization repository

## Status

Accepted

## Context

Purpose-specific ports existed for execution storage, lifecycle, operator control, administration,
and result promotion, but the execution and lifecycle SQLite adapters still delegated to one broad
repository. That intermediate interface mixed queue claiming, state mutation, checkpoint writes,
ranked results, JSON rows, and OOS metrics. It no longer represented an application abstraction and
made transaction ownership indirect.

## Decision

Remove `IOptimizationRepository` and `OptimizationRepository`. Each purpose-specific SQLite adapter
uses `IDbContextFactory<AppDbContext>` directly and updates only the columns it owns.

`OptimizationJobLifecycle` owns conditional Pending-to-Running claims and terminal/restart state
updates. Its executor-originated writes require the row to remain Running, so a concurrent operator
pause or cancellation cannot be overwritten. `OptimizationJobExecutionStore` owns progress, OOS metrics, and the transaction that joins
ranked-result persistence to its following checkpoint. `OptimizationResultPersistence` contains the
infrastructure-only ranking merge shared inside that transaction.

## Consequences

- Every optimization persistence operation has one named purpose-specific owner.
- Queue claims and chunk commits remain atomic without a pass-through abstraction.
- Progress and lifecycle writes cannot overwrite unrelated job settings or promotion metadata.
- Tests exercise the real SQLite adapters instead of mocking a broad infrastructure repository.
- Adding a persistence operation now requires choosing the application capability that owns it.
