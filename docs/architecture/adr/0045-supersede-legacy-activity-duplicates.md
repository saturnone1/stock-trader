# ADR 0045: Preserve and supersede legacy activity duplicates

## Status

Accepted

## Context

Before deterministic `SignalBarAt` and recommendation `SourceSignalId` identities existed, the
daily scanner could persist the same observed signal and recommendation repeatedly. Production
contains legacy rows with null identities and identical strategy, symbol, price geometry, and UTC
day. Current writes are protected by unique indexes and idempotent stores, but read screens,
dashboard counts, reports, and manual-order lookup still treated every legacy row as independent.

Deleting the rows would remove audit evidence, and grouping every null-identity row at read time
would duplicate compatibility logic across several adapters and could hide legitimate activity on
different days.

## Decision

- `PatternSignal` and `TradeRecommendation` persist an `IsSuperseded` audit state.
- An ordered EF migration marks only older null-identity rows that have exact strategy/symbol/price
  geometry on the same UTC day. The latest row remains operational.
- Recommendations are eligible for supersession only when they are unexecuted, have no entry claim,
  and have no broker order evidence. Executed or uncertain orders are never reclassified.
- The migration never deletes activity. It is deterministic and idempotent for a fixed database.
- Signal browsing, manual signal execution, recommendation browsing, dashboards, daily reports,
  and live evaluation exclude superseded rows at their persistence adapters.
- New identified activity continues to rely on `SignalBarAt`, `SourceSignalId`, and their unique
  indexes; supersession is a compatibility state, not the primary idempotency mechanism.

## Consequences

Operator views and reports no longer count repeated pre-identity scanner observations as separate
opportunities, while every original row remains queryable for audit and rollback. Schema and data
adoption run through the canonical EF migration and deployment backup path. Freshness and expiry of
otherwise unique active signals remain a separate policy decision.
