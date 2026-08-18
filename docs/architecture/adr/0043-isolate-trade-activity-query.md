# ADR 0043: Isolate trade activity queries and contracts

## Status

Accepted

## Context

The trade HTTP module queried persistence ports directly and owned recommendation execution-status
calculation, risk/reward projection, query parsing, pagination defaults, and completed-trade holding
days. Both recommendation and history responses were anonymous objects, so OpenAPI could not give
the desktop a stable response type. The Svelte pages consequently read both Pascal-case and
camel-case fields and silently tolerated contract drift.

Invalid pattern and date text was ignored, while invalid or excessive pagination reached the
persistence adapter. This made malformed research and operator queries look like valid unfiltered
results or produce provider-specific failures.

## Decision

- `ITradeActivityQuery` owns recommendation and completed-trade read models, one observation time,
  status and ratio projection, holding days, and validated pagination defaults.
- `ITradeActivityStore` is a purpose-specific persistence port. Its SQLite adapter projects EF rows
  into storage-independent activity records and applies stable timestamp-plus-ID ordering.
- The HTTP adapter binds typed pattern and date inputs, maps explicit response contracts, and
  translates application validation outcomes to HTTP 400.
- Page sizes must be between 1 and 500, skip must be non-negative, and a start date cannot follow
  the end date. Unknown numeric pattern codes are rejected by the application; invalid enum/date
  text is rejected by ASP.NET binding instead of being ignored.
- The desktop consumes generated camel-case recommendation and history contracts without legacy
  casing fallbacks. Stable strategy/order codes remain in the contract while investor-facing names
  come from the central domain catalogs; custom trades use their stored strategy name.
- Existing trading write ports and full-history analysis ports remain intact during the staged
  migration; this read boundary does not change order placement or strategy execution.

## Consequences

The trade endpoint no longer owns trading calculations or persistence access, and the UI contract
is visible in generated OpenAPI. Requests that were previously ambiguous now fail closed. Default
recommendation and history page sizes remain 50, while explicit requests are bounded to protect the
operator API. Stable ID tie-breaking makes repeated pages deterministic.
