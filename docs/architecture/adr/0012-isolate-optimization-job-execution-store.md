# ADR 0012: Isolate optimization job execution storage

## Status

Accepted

## Context

After candidate evaluation and data preparation moved behind application ports, the background
executor still referenced `IOptimizationRepository`, persistence entities, parameter JSON, status
enums, and every OOS database column. The worker therefore mixed job sequencing with SQLite data
shape and could accidentally overwrite in-sample fields while updating OOS metrics.

Chunk persistence was also repeated for coarse and fine stages: map successful results, merge the
ranked rows, advance tested combinations, and save the restart checkpoint.

## Decision

`IOptimizationJobExecutionStore` is the purpose-specific application port for control signals,
progress checkpoints, chunk results, stored candidate parameters, and OOS metric updates.

`OptimizationJobExecutionStore` is the infrastructure adapter. It translates application results
to `OptimizationResult`, owns parameter JSON compatibility, maps persisted pause/cancel states, and
persists only its owned columns through `IDbContextFactory`. Malformed historical parameter JSON is
skipped as before. ADR 0018 removed the temporary broad repository delegation.

OOS persistence uses a targeted database update for only the nine OOS columns.
The executor no longer loads and writes a complete persistence entity merely to attach validation
metrics. Coarse and fine stages call the same `SaveChunkAsync` contract with their next restart
checkpoint.

## Consequences

- The executor contains no repository, persistence-result, or JSON dependency.
- IS fields cannot be unintentionally changed by the OOS update path.
- Result serialization and legacy-row compatibility have one infrastructure owner.
- The existing merge-then-progress ordering and restart semantics remain unchanged.
