# ADR 0013: Isolate optimization job lifecycle

## Status

Accepted

## Context

`ContinuousOptimizationService` directly loaded the next persistence entity, set `Running`, and
later mutated `Completed`, `Cancelled`, `Pending`, or `Failed` fields. The executor also accepted the
same database-shaped `OptimizationJob`. Both workers therefore depended on the broad repository,
storage status enum, error columns, and navigation-capable entity even after result storage had been
isolated.

State transition code was difficult to replay with a fixed clock because scheduling, scoped
repository resolution, logging, and persistence mutation were interleaved.

## Decision

`IOptimizationJobLifecycle` owns queue selection and all execution state transitions. It returns an
`OptimizationJobExecutionTicket`, an application snapshot containing only values required by the
executor. The ticket carries mutable progress counters so the scheduler can report the latest chunk
when shutdown interrupts a job, but contains no persistence navigation or status fields.

The SQLite `OptimizationJobLifecycle` adapter maps the selected entity to a ticket and applies:

- Pending to Running with the injected observation time;
- Completed or Cancelled with completion evidence and a cleared prior error;
- process shutdown back to Pending with terminal evidence cleared;
- execution failure to Failed with its timestamp and message.

A paused disposition performs no write because the external pause command has already persisted the
authoritative status. `ContinuousOptimizationService` retains polling, retry delays, logging, scope
creation, executor invocation, and automatic-promotion coordination.

## Consequences

- Neither background optimization component imports `Data` or `Models`.
- Lifecycle transitions are directly testable with fixed timestamps.
- The executor cannot accidentally mutate unrelated job columns.
- Queue claiming is implemented by the persistence adapter and is strengthened by ADR 0014.
