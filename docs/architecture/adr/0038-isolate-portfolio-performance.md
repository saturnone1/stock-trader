# ADR 0038: Isolate portfolio read projections

## Status

Accepted

## Context

`PortfolioEndpoints` loaded trade and pattern-stat entities, calculated win/loss averages, ordered
the equity curve, and calculated maximum drawdown inside the HTTP handler. The drawdown algorithm
started cumulative PnL and its peak at zero. An opening loss therefore reported zero drawdown until
cumulative profit first became positive. It also loaded only the first 1,000 trades through the
store's default page size, so long-running accounts silently omitted older history.
Portfolio, trade, and dashboard endpoints also loaded open-position entities independently and
sampled the clock while mapping each row, allowing one response to contain slightly different
observation instants.

## Decision

- `IPortfolioPerformanceQuery` exposes one storage-independent application snapshot for portfolio
  performance.
- `IOpenPositionQuery` is the sole read projection for current positions used by portfolio, trade,
  dashboard, and risk-overview APIs. It samples one observation instant and applies the central
  durable-order status policy to every row.
- `PortfolioPerformancePolicy` owns deterministic ordering, win/loss summaries, the cumulative PnL
  curve, and maximum drawdown.
- Maximum drawdown starts from the validated user account size and applies realized dollar PnL in
  `ExitTime`, then persisted trade-ID order. This recognizes losses before the first profitable
  trade and makes equal-time ordering reproducible.
- The query explicitly loads complete trade history rather than inheriting the paged read default.
- Pattern-stat and user-settings reads remain sequential because their legacy scoped adapters share
  one EF context; the independent trade-history read may overlap them safely.
- The API maps application snapshots to explicit response records and owns no position-status or
  performance arithmetic and no pattern-stat persistence dependency.

## Consequences

Portfolio performance is reproducible outside ASP.NET and can be shared with future reports without
copying formulas. Historical maximum-drawdown values can change because the previous zero-based
calculation was not an account-equity drawdown and omitted opening losses. Accounts with more than
1,000 completed trades now include their entire persisted history. Characterization tests name and
preserve both corrections. All open-position screens now share the same holding-day, pending-order,
and total-unrealized-PnL observation.
