# ADR 0064: Publish risk state as one application generation

## Status

Accepted

## Context

Runtime risk management was a singleton Services class that opened dependency-injection scopes to
locate settings and open-position repositories. It also called the account and broker layer while
owning trading-halt and portfolio aggregation rules. This inverted the intended dependency flow and
made the use case difficult to construct or reason about without the complete host container.

The singleton updated account states one at a time and published the portfolio state afterward.
Concurrent API, signal-evaluation, or monitor reads could therefore combine accounts and portfolio
values from different evaluation cycles. When every account was disabled, only a fallback field was
updated and the public portfolio snapshot could retain an older loss result indefinitely.

## Decision

- Runtime risk contracts, options, calculations, and orchestration belong to `Application/Risk`.
- A scoped `IRiskManagementDataSource` adapter owns account, broker, settings, position, and cache
  access and returns storage-independent evidence records. Its short-lived position-cache duration
  comes from validated trading options.
- `IRiskManagementService` is scoped and receives its data source directly. It does not create
  scopes, locate services, use configuration providers, or depend on persistence and broker types.
- One singleton `RiskStateStore` contains an immutable generation of account, portfolio, and
  fallback snapshots. A refresh builds the complete generation locally and publishes it with one
  volatile write.
- Broker-reported zero daily PnL remains authoritative. Position PnL is used only when broker
  evidence is absent. Accountless legacy positions remain assigned to the first enabled account
  exactly once.
- With no enabled account, current position evidence becomes both fallback and portfolio state so
  stale portfolio risk cannot survive account removal or disablement.

## Consequences

API, background monitoring, and signal evaluation depend on one application contract and share the
same process-wide risk generation without a singleton service locator. Scoped persistence lifetimes
are validated normally. Existing position limits, sector limits, sizing, broker fallback, and legacy
account assignment formulas are unchanged; only the stale no-account portfolio behavior is corrected
and protected by a named regression test.
