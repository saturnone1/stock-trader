# ADR 0041: Isolate the dashboard read model

## Status

Accepted

## Context

`DashboardEndpoints` directly coordinated seven services and repositories, called the active broker,
and constructed an anonymous response. The desktop then translated that untyped response into a
handwritten `DashboardData` model. During translation it set total exposure to zero, relabelled a
negative daily return as maximum drawdown, and reduced risk to a fabricated binary label. Those
values looked authoritative but were not calculated by any trading or portfolio policy.

The endpoint also loaded positions separately from the risk overview even though both screens need
the same observation time and durable order status.

## Decision

- `IDashboardQuery` returns one storage-independent read model composed from the active broker
  account, the existing risk overview, dashboard activity, and market regime.
- `RiskOverviewSnapshot` carries its source `OpenPositionListSnapshot` and order mode so dashboard
  composition reuses the same position observation and settings read.
- `IDashboardActivityStore` owns the exact active-signal count and stable latest-recommendation read.
- `IActiveBrokerAccountQuery` maps broker state once and is also reused by daily-report equity reads.
- `DashboardResponse` is an explicit OpenAPI contract. The desktop consumes its generated type
  directly and does not invent exposure, drawdown, or risk-level fields.
- `RiskRewardRatioPolicy` and `PositionReturnPolicy` are the single calculation owners for displayed
  recommendation R/R and open-position return.

## Consequences

The dashboard now labels and displays only observed values: daily PnL, daily return, unrealized PnL,
open positions, active signals, execution mode, and trading-halt state. The former “Total Exposure”
value was always zero and the former “Max Drawdown” was merely the negative daily return; both are
removed rather than presented as real risk metrics. The API endpoint is a thin explicit-contract
adapter, while architecture and characterization tests prevent handwritten dashboard schemas and
fabricated risk values from returning.
