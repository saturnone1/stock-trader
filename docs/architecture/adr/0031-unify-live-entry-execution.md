# ADR 0031: Unify automatic and manual live-entry execution

## Status

Accepted

## Context

Automatic and user-triggered entries independently selected a broker, submitted an order, polled the
broker position, marked a recommendation executed, and saved a local position. The active broker and
active account were resolved in separate calls, so an account switch during submission could assign
the resulting position to a different account. The broker port returned only a boolean and discarded
the order ID and acceptance state. Manual entry also queried EF directly and updated recommendation
and position rows in separate database commits.

## Decision

- `IAccountManager.GetBrokerContextAsync` resolves an enabled account and its cached broker as one
  snapshot. Entry orchestration carries that snapshot through submission and persistence.
- `IBrokerService.SubmitEntryOrderAsync` returns `BrokerOrder` evidence or null. Implemented adapters
  retain the broker order ID, symbol, quantity, status, type, and submission time instead of reducing
  acceptance to a boolean.
- Terminal rejection statuses remain rejected. A non-terminal response whose symbol, long direction,
  or quantity differs from the request fails closed as accepted-but-untracked so it cannot create an
  incorrect local position or invite a duplicate retry.
- `LiveEntryExecutionCoordinator` is the sole automatic/manual path for broker submission, position
  confirmation, fill repricing, account ownership, and local commit.
- Every recommendation is persisted before the external order side effect. Rejected manual orders
  therefore remain as unexecuted audit records.
- `ILiveEntryExecutionStore` atomically marks the persisted recommendation executed and inserts its
  position. It rejects a missing or already-executed recommendation and invalidates the shared read
  caches only after commit.
- An accepted broker order followed by confirmation, cancellation, or persistence failure has an
  explicit `BrokerAcceptedTrackingFailed` result. Callers report it as accepted and instruct the user
  not to retry; it is never collapsed into a rejection.
- `IManualOrderSignalStore` removes EF access from the manual workflow.

## Consequences

Automatic and manual entry now share one fill and persistence sequence, and the stored account ID is
the account whose broker accepted the order. Recommendation and position state cannot be partially
committed locally. Broker acceptance evidence survives the coordinator result and critical log when
local tracking fails. Manual broker rejections now add an unexecuted recommendation audit row, which
is an intentional observable change. No schema migration is required.

This decision does not yet provide a durable pre-submission claim or restart-time reconciliation for
new entries. A process failure after broker acceptance but before local commit remains detectable in
broker history and critical logs but requires operator reconciliation; durable entry claims are a
separate follow-up because they require persisted lifecycle fields and recovery rules.

ADR 0032 supersedes this final limitation with a durable claim and restart reconciliation lifecycle.
