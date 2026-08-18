# ADR 0052: Remove account-ambiguous order cancellation

## Status

Accepted

## Context

`IOrderService.CancelOrderAsync` accepted only a broker order identifier and selected whichever
trading account happened to be active. Broker order identifiers are scoped by broker account, so
that input could not prove which account owned the order. The method had no API, worker, desktop, or
application caller, but retaining it made a future cross-account cancellation easy to introduce.

Operator full exits and entry/position reconciliation already resolve durable recommendations and
positions first, then select their stored owning accounts. Those flows fail closed when ownership is
missing or ambiguous.

## Decision

- Remove `CancelOrderAsync` from `IOrderService` and `OrderService` rather than preserve an unsafe,
  unused compatibility surface.
- Keep cancellation on the account-bound `IBrokerService` adapter and in the central broker
  capability catalog. A future cancellation use case may call it only after resolving a durable
  order reference that includes the owning account.
- Keep the explicit active-account fallback only for legacy positions whose stored `AccountId` is
  zero. New order-lifecycle operations may not infer ownership from the active account.
- Enforce the boundary with an architecture test that rejects both the order-ID-only service
  contract and active-broker selection inside `OrderService`.

## Consequences

There is no public HTTP or desktop contract change because the removed method had no caller. A future
cancel button requires a purpose-specific application command and durable account identity before it
can reach a broker. This prevents an active-account switch from redirecting cancellation to another
account while preserving broker-adapter capability for a correctly scoped implementation.
