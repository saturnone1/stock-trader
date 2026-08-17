# ADR 0005: Version persisted strategy documents

## Status

Accepted

## Context

Custom strategies are persisted as an EF entity with several JSON fragments. The compiled runtime
already has an engine schema version, but stored rows and API payloads did not identify the document
format that produced those fragments. A future structural change could therefore be interpreted with
new semantics without a reliable compatibility decision.

## Decision

Persist `DocumentVersion` on every custom strategy independently from the compiled-engine
`SchemaVersion`. Version zero represents only the pre-versioned compatibility input. The application
may read that legacy form, but every successful create or update stamps the current version. Unknown
future versions fail validation and are excluded from execution rather than being interpreted as the
current format. Existing production rows receive version one through an ordered EF migration.

Compatibility transformations belong in `StrategyDocumentVersionPolicy` (or a successor owned by
the same application boundary), not in API endpoints, repositories, or individual execution paths.

## Consequences

- Preview, backtest, optimization, and live execution can make the same explicit compatibility
  decision before compiling a strategy.
- Future document changes require a named version transition and tests instead of silent JSON drift.
- The document version and compiled-engine version may advance independently.
- Removing a legacy reader requires evidence that no persisted or imported documents still need it.
