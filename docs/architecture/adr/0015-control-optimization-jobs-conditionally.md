# ADR 0015: Control optimization jobs conditionally

## Status

Accepted

## Context

Pause, resume, and cancel endpoints previously loaded an EF job, checked its status, mutated the
entity, and saved the complete row. The check and write were separate operations. A worker or a
second operator command could change the row between them, after which the stale entity update could
overwrite that newer state and unrelated columns.

Startup recovery also reached through the broad optimization repository even though it needed only
one well-defined Running-to-Pending recovery operation.

## Decision

`OptimizationJobControlPolicy` is the application owner of legal user transitions:

- Pending or Running to Paused;
- Paused to Pending;
- any non-terminal state to Cancelled with the supplied observation timestamp.

`OptimizationJobControlService` reads the current application state, resolves a transition, and
asks `IOptimizationJobControlStore` to persist it only if the stored state still matches the state
that was evaluated. A failed conditional write is reported as a concurrent change rather than
silently retried over newer evidence.

The SQLite adapter uses set-based conditional updates for user commands and interrupted-job startup
recovery. API observation and projection times come from `TimeProvider`; the API no longer reads the
system clock directly for optimization jobs.

## Consequences

- HTTP adapters no longer implement pause, resume, or cancel business rules.
- Concurrent commands have one winner and cannot overwrite a newer terminal or paused state.
- Application policy and ports contain no EF or persistence status types.
- Startup recovery depends on the purpose-specific control use case.
- Job creation, queries, deletion, and automatic promotion still use the broad legacy repository and
  remain scheduled extraction work.
