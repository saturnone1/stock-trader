# ADR 0021: Own market-data identity in Domain

## Status

Accepted

## Context

`TimeFrameCatalog` and `DataProviderCatalog` were already the central sources of timeframe and
provider capability metadata, but their identity enums still lived in `Models.Enums`. That made the
Domain layer depend on a broad legacy model namespace and left ownership ambiguous for every API,
provider, strategy, and persistence adapter using those values.

The enum member order is persisted as integers in existing SQLite rows and their names are emitted
through JSON enum conversion, so changing either would be a trading-data compatibility change.

## Decision

- `TimeFrame` and `DataSource` are owned by `Domain.MarketData` beside their catalogs.
- Their member names and declaration order remain unchanged, preserving database and JSON values.
- The application and test projects import `Domain.MarketData` globally during the staged migration;
  adapters may keep importing other legacy enums without creating a second market-data identity.
- An architecture test rejects any `StockTrader.Models` dependency from Domain and verifies that the
  legacy enum files do not return.

## Consequences

Domain now owns the identifiers used to select bar cadence and market-data provider capabilities,
while provider implementations remain adapters. Future extraction of deterministic Engine code can
depend on these identities without importing the legacy model layer. The global import is a
transition aid, not a compatibility type: there is only one runtime enum for each identity.
