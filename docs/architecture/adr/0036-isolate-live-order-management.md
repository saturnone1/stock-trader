# ADR 0036: Isolate operator live-order management

## Status

Accepted

## Context

`OrderEndpoints` directly selected brokers, searched mutable position entities, interpreted durable
entry and position state, called execution coordinators, and authored operator messages. Manual
position exits and reconciliation also selected the currently active broker instead of the broker
owned by the persisted position. A position belonging to another account could therefore be sent to
the wrong broker account.

## Decision

- `ILiveOrderManagement` is the application-facing use-case port for manual full exit, position-order
  reconciliation, and entry-order reconciliation.
- `LiveOrderManagement` owns position uniqueness checks, durable account routing, execution
  coordinator calls, and stable operator-facing outcome messages.
- Persisted `AccountId` selects the broker for a position. Only legacy `AccountId == 0` rows fall back
  to the active account.
- `IAccountManager.GetBrokerContextForPositionExitAsync` permits risk-reducing exits for disabled
  accounts without permitting new entries. Reconciliation retains its separate read/recovery path.
- The API adapter binds explicit request contracts and maps typed use-case outcomes to HTTP status
  codes and explicit response contracts.

## Consequences

`OrderEndpoints` no longer imports account management, trading persistence, or live execution
coordinators. Automatic and operator exits still share `ILivePositionExecutionCoordinator`, but the
operator path can no longer silently choose a different account. Architecture tests enforce the
thin endpoint boundary and account-routing goldens preserve fail-closed duplicate and missing-account
behavior.
