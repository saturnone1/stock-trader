# ADR 0008: Separate executable strategy documents from storage entities

## Status

Accepted.

## Context

Preview accepted an explicit write contract, but backtest and optimization serialized
`CustomPatternDefinition`, the EF entity. The compiler, detector factory, optimization variant
builder, and compiled runtime also accepted that entity. A database-only field could therefore
silently become part of the desktop API, and research execution depended on storage shape.

## Decision

`Application.Strategies.StrategyDocument` is the mutable, versioned source document shared by
preview, backtest, optimization, scanning, and live execution. It contains strategy semantics and
an optional `StoredStrategyId` reference, but no normalized database key or audit timestamps.

`StrategyCompiler` and custom detector contracts accept only `StrategyDocument`. Strategy CRUD uses
`StoredStrategy`, which combines the document with server-owned identity and audit timestamps.
`ICustomPatternStore` exposes only these application types; EF entities are converted inside the
SQLite adapter. Backtest and optimization OpenAPI schemas
refer to `StrategyDocument`; the desktop explicitly maps a stored response `id` to
`storedStrategyId` and removes storage audit fields before execution requests.

The EF entity is confined to `Data`, database configuration, and migrations. Architecture tests
reject any `CustomPatternDefinition` reference under `Application`.

## Consequences

- Adding an EF column cannot alter research or execution API contracts.
- Inline research strategies and stored strategies compile through the same path.
- Continuous optimization can reliably promote results through an explicit stored-strategy
  reference instead of depending on a name match.
- Mapping code is explicit and tested, adding a small amount of deliberate duplication at the
  storage boundary.
