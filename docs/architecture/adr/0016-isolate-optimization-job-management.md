# ADR 0016: Isolate optimization job management

## Status

Accepted

## Context

The optimization job HTTP module created and mutated EF entities, deserialized persisted parameter
JSON, calculated search-space size and progress projections, filtered persistence status enums, and
deleted rows through the broad repository. The API therefore owned application policy and storage
compatibility while exposing a large change surface.

The result mapper also omitted `OptimizationResult.Id`. The desktop's “apply this result” action
read `result.id`, received `undefined`, and sent `null`; the server then selected its automatic best
candidate instead of the row the operator chose.

## Decision

`OptimizationJobManagementService` owns creation defaults, fixed-clock timestamps, search-space
counting, state-filter parsing, progress and remaining-time projections, settings validation, and
terminal deletion policy. It exchanges `OptimizationJobRecord` through the storage-independent
`IOptimizationJobManagementStore`.

The SQLite adapter maps EF jobs and results explicitly, owns legacy parameter JSON compatibility,
updates only requested settings columns, and deletes a terminal job only while its stored state
still matches the state evaluated by the use case. One infrastructure state mapper converts between
the application and persistence enums.

The HTTP module maps application views to explicit response DTOs and retains only status-code and
message concerns. Stored optimization result responses include their database ID; synchronous
in-memory optimization results leave that optional field null.

## Consequences

- Job administration no longer exposes EF entities or persisted JSON to the HTTP adapter.
- Combination-count and projection formulas have named application owners and fixed-time tests.
- Concurrent state changes cannot be overwritten by terminal deletion.
- Manual result application now targets the operator-selected result row.
- The broad legacy repository loses unused job detail, list, and delete methods; execution lifecycle
  and automatic promotion remain scheduled for their own purpose-specific persistence ports.
