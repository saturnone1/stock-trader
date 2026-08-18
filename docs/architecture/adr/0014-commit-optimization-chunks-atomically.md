# ADR 0014: Commit optimization chunks atomically

## Status

Accepted

## Context

The background optimizer previously selected a Pending row and changed it to Running in two
independent database operations. Two workers could therefore select the same job before either
status update became visible.

Each completed search chunk also merged its ranked results and advanced `TestedCombinations` and
`CurrentChunkIndex` through separate transactions. A process failure between those writes could
leave new results beside an old restart checkpoint. The next process would repeat the chunk and no
longer have a single durable boundary describing completed work.

## Decision

The SQLite lifecycle adapter claims a candidate with a conditional update whose predicate includes both
the candidate ID and Pending status. Only the worker whose update affects one row receives the
execution ticket. A losing worker queries the remaining queue again.

`SaveChunkAsync` owns the durable chunk boundary. It loads the existing ranked rows, combines and
trims them with the unchanged ranking rules, updates the job's tested count, chunk index, and
observation timestamp, and saves all changes in one transaction. This operation is used even when a
chunk produces no ranked result, because advancing the checkpoint remains a durable event.

## Consequences

- One queued job cannot be started by two concurrent workers.
- A restart sees both a chunk's retained results and its following checkpoint, or neither.
- External pause/cancel status remains untouched by checkpoint writes.
- SQLite success, concurrency, and failure-injection tests guard the persistence semantics.
- Cross-process leasing and abandoned-worker timeouts remain separate operational concerns; the
  existing startup recovery continues to return Running jobs to Pending after a process restart.
