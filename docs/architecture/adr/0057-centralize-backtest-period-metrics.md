# ADR 0057: Centralize backtest period metrics

## Status

Accepted

## Context

Backtest CAGR was calculated inside the service-layer performance helper. Its duration started at
the first trade entry and ended at the last trade exit. A strategy could therefore report a higher
annualized return simply by remaining in cash for part of the requested evaluation range. Calmar
inherited the same distortion because it divides annualized return by maximum drawdown. Headline
Sharpe and Sortino also estimated annual trade frequency over only the active-trade interval.

Percentage values also crossed this calculation in two representations: portfolio results use a
fraction (`0.10 = 10%`), while the public annualized-return field uses percentage points
(`10 = 10%`). The conversion was correct but implicit at the service boundary.

## Decision

- `BacktestPerformancePolicy` is the single owner of period-based CAGR, Calmar, Sharpe, and Sortino
  calculations.
- Policy inputs and its annualized-return output use fraction units. Parameter and result names
  state that contract.
- The explicit requested `From`/`To` evaluation range defines elapsed calendar time. Trade dates
  do not define metric duration.
- `BacktestResultBuilder` converts the annualized fraction to percentage points only when filling
  the existing public `AnnualizedReturn` field. The HTTP contract therefore keeps its established
  unit.
- Complete loss is capped at `-1` in fraction form, and a non-positive duration returns zero.
  Sub-day annualization uses a named one-day floor, preserving the historical guard against
  explosive intraday exponents. CAGR above one million percent is capped because larger values are
  not decision-useful and cannot safely cross decimal result contracts.
- An architecture test prevents CAGR or Calmar ownership from drifting back into the general
  trade-statistics helper.

## Consequences

CAGR, Calmar, Sharpe, and Sortino can decrease for strategies whose first trade occurs after the
requested start or whose final trade exits before the requested end. This is an intentional
correction: inactive cash time is part of the strategy result. Total return, maximum drawdown,
public field units, and the database schema do not change.
