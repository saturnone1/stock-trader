# ADR 0028: Isolate research universe queries

## Status

Accepted

## Context

The universe and financial-factor HTTP modules queried EF entities directly, calculated market-cap
percentiles and financial growth inside route handlers, duplicated CSV parsing, and returned
anonymous response shapes. File parsing and SEC synchronization also depended on an API import DTO,
while manual imports used system time and accepted an `AppDbContext` from the endpoint. Research
selection behavior therefore had no application owner and storage, HTTP, and provider concerns
pointed in both directions.

## Decision

- `IResearchUniverseStore` exposes detached active-ticker, financial-snapshot, import-run, and
  financial-upsert models. `ResearchUniverseStore` is the only EF adapter for this boundary.
- `ResearchUniverseQueryService` owns market-cap coverage, percentile ranking, facets, search,
  filters, limits, and sorting.
- `FinancialFactorQueryService` owns deterministic latest-snapshot selection, growth and
  turnaround calculation, filter order, comparison summaries, and import-run reads.
- `ResearchFilterPolicy` is the shared case-insensitive CSV selection rule for universe, factor,
  and manual vendor-sync requests. `ResearchUniversePolicy` names the shared default, maximum,
  facet, and recent-run limits instead of leaving route-level magic numbers.
- `FinancialSnapshotImportService` uses `MarketSymbolPolicy` and injected `TimeProvider` before
  passing normalized snapshots to the store. File and SEC adapters use the application import
  model instead of an HTTP type.
- HTTP routes bind parameters, call application services, and return explicit contracts that
  preserve the existing camel-case field names and date-string formats.

## Consequences

Research ranking and factor arithmetic can be verified without a database or web host, SQLite
mapping can evolve independently, and generated clients now see response schemas instead of
anonymous objects. Duplicate snapshots on the same date are selected deterministically by update
time and persistent sequence id, while duplicate import rows for the same normalized symbol and
date collapse deterministically to the last supplied value instead of violating the database
constraint. Background ingestion still owns file-run orchestration and SEC transport; those are
infrastructure concerns outside the query policy.
