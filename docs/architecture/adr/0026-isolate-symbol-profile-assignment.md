# ADR 0026: Isolate symbol profile assignment

## Status

Accepted

## Context

The symbol-profile HTTP module read and mutated EF entities directly, parsed strategy enum strings
inside the endpoint, used system time, and returned anonymous objects. Live pattern detection also
queried the same table directly and privately selected the newest active row. Invalid strategy
codes or malformed configuration could therefore fail as server errors, while API and live scanning
had no enforced shared ownership of assignment semantics.

## Decision

- `SymbolProfileManagementService` owns symbol/name normalization, defaults, validation, merge
  semantics, activation commands, and modification time.
- `ISymbolProfileStore` carries detached application snapshots. `SymbolProfileStore` alone maps EF
  entities and atomically deactivates the previous profile when another profile is activated.
- Live pattern detection reads the active assignment through the application service and does not
  reference EF.
- HTTP routes use explicit request and response contracts while preserving existing JSON enum and
  date formats.
- `MarketSymbolPolicy` is the shared symbol normalization and validation rule for settings, market
  synchronization, preview, backtest, profile assignment, and live detection.
- Assignable built-in strategies come only from `PatternCatalog`.

## Consequences

Unsupported strategies, malformed JSON, invalid risk values, and invalid date ranges fail before
persistence. The API, research paths, and live scanner use the same symbol identity, and persistence
details can change without changing the application or HTTP contracts.
