# ADR 0030: Isolate trading-account state and broker construction

## Status

Accepted

## Context

`AccountManager` directly created EF contexts, read the system clock, enforced the single-active
account rule, constructed concrete broker clients, retained a runtime cache, and returned persistence
entities through the HTTP API. Broker choices and environments were duplicated in the desktop, and
the generated OpenAPI document did not describe account request or response bodies. This made an
account UI change capable of changing persistence or live broker behavior without a focused test.

## Decision

- `BrokerCatalog` is the domain owner of stable broker IDs, display metadata, supported environments,
  credential requirements, and implementation availability. Existing integer IDs remain unchanged.
- `ITradingAccountStore` is the application persistence port. `TradingAccountStore` is the only EF
  adapter and owns atomic single-active transitions, deterministic promotion after deletion, and
  connection timestamps.
- `TradingAccountPolicy` validates account commands without HTTP or EF dependencies. Alpaca accounts
  fail closed when credentials or a supported environment are missing, and disabled accounts cannot
  be active.
- `AccountManager` coordinates the store, injected clock, and broker cache. Concrete construction is
  delegated to `IAccountBrokerServiceFactory`, and account changes invalidate affected cached clients.
- Account endpoints use explicit write and read contracts. Secrets are write-only, API keys are
  masked, and OpenAPI describes metadata, validation errors, and all account operations.
- The desktop derives broker and environment options from server metadata and consumes the explicit
  camel-case response contract.

## Consequences

Account persistence and broker SDK changes can evolve independently. The active-account invariant is
transactional and directly tested against SQLite, while clock and cache behavior are deterministic in
unit tests. Creating an Alpaca account without both credentials now returns validation errors instead
of storing an unusable account. No database migration is required because broker numeric values and
the existing entity schema are preserved.
