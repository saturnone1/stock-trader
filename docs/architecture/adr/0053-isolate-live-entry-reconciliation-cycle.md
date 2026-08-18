# ADR 0053: Isolate the live entry reconciliation cycle

## Status

Accepted

## Context

`EntryExecutionReconciliationService` combined host scheduling with pending-entry persistence,
durable account routing, broker capability checks, order-history windows, and reconciliation. This
made the behavior difficult to test without a hosted service. Its delay also ignored the injected
`TimeProvider`, and its embedded interval clamp silently changed configured values. A failure while
reading one account's order history aborted the complete cycle, delaying reconciliation for every
other account.

## Decision

- `EntryExecutionReconciliationService` is a small scheduling adapter. It creates a scope, invokes
  `ILiveEntryReconciliationCycle`, and waits through the injected `TimeProvider`.
- `LiveEntryReconciliationCycle` loads one configured batch, validates durable account/request-time
  evidence, groups entries by `EntryAccountId`, and reads each owning broker's order history once.
- One observation instant supplies the upper order-history boundary for every account in a cycle.
- Account lookup and order-history failures are isolated per account group. A failed account cannot
  prevent healthy accounts from reconciling.
- Missing durable ownership or request time fails closed and never falls back to the active account.
- The supported 5–300 second scheduling range is expressed as named `TradingSettings` invariants and
  validated at startup instead of being silently clamped in the worker.

## Consequences

The hosted service contains no persistence, broker, account-routing, or reconciliation policy. The
cycle can be tested directly for cross-account evidence isolation and partial provider failures.
Misconfigured intervals fail during startup with an actionable validation error. There is no public
HTTP, desktop, database-schema, or strategy-result change.
