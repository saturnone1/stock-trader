# ADR 0033: Centralize operation-specific broker capabilities

## Status

Accepted

## Context

A single `IsImplemented` flag could not distinguish account and position reads from entry, scaling,
exit, cancellation, and order-history support. Runtime adapters still exposed every method, so a
stub or partially implemented broker could be called and only fail after an execution claim was
persisted. A second keyed-DI broker factory also duplicated the account-owned construction path.

## Decision

- `BrokerCatalog` owns explicit capabilities for account reads, position reads, order history,
  protected entry, scale-in, full exit, partial exit, and cancellation. A protected entry means the
  broker submission preserves the strategy's stop-loss and profit-target contract.
- Each runtime adapter exposes only its stable broker identity. Callers derive capabilities from the
  catalog rather than copying flags into adapters or UI code.
- Entry and position coordinators reject unsupported submissions before acquiring durable claims.
  Reconciliation requires order-history support unless the caller already supplies known evidence.
- Account validation and activation prevent unavailable integrations from becoming live accounts.
  Disabled placeholder accounts remain valid for configuration migration.
- The account metadata contract projects the same capability object to the desktop, which labels
  supported actions and disables unavailable connection or activation controls.
- The unused default-broker setting, keyed registrations, and legacy factory are removed. The
  account-scoped factory is the only production broker construction path.

## Consequences

The UI and server agree on what a broker can actually do, while server-side checks remain the safety
boundary. Unsupported operations cannot leave false pending-order state or reach a stub adapter.
Adding partial broker support now requires one catalog change plus adapter implementation and tests,
instead of an all-or-nothing implementation flag or another construction switch.
