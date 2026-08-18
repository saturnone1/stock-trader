# ADR 0023: Own pattern identity and display metadata in Domain

## Status

Accepted

## Context

`PatternType` lived in the broad persistence-model namespace even though preview, backtest, live
execution, API contracts, and notifications all use it as strategy identity. Telegram maintained a
partial Korean-name switch, Discord exposed raw enum names, and newer strategies therefore appeared
differently depending on the delivery channel.

The enum's numeric values are persisted in SQLite and its names are public JSON codes. Either kind
of change would break existing strategy and trading data.

## Decision

- `PatternType` is owned by `Domain.Strategies`; all existing names and numeric values are frozen.
- `PatternCatalog` is the single mapping from identity to stable code, investor-facing display name,
  and built-in execution support.
- Strategy-builder metadata projects the catalog for desktop consumers.
- Telegram and Discord resolve display names through the catalog. A custom recommendation uses its
  stored strategy name when present.
- Detector implementation types remain in the infrastructure-facing detector catalog; its coverage
  test derives the expected inventory from the domain catalog.

## Consequences

Adding a strategy now requires one explicit domain metadata entry and one detector registration for
an executable built-in strategy. Coverage tests fail when either inventory is incomplete. Storage
and API clients continue to see the same enum codes, while user-visible terminology no longer
drifts between channels.
