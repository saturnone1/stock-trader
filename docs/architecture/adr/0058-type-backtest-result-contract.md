# ADR 0058: Type the backtest result contract

## Status

Accepted

## Context

The backtest endpoint returned an anonymous object assembled inside the HTTP handler. OpenAPI could
not provide one reusable response schema, so the desktop called it with `any` and also retained an
unrelated manual `BacktestResult` interface. The simulation calculated Sortino, Calmar, profit
factor, CAGR, Kelly sizing, MAE/MFE, survivorship warnings, and regime statistics, but the endpoint
discarded those fields. The desktop contained a regime panel that could never receive data.

Trade projection recalculated return from entry and exit prices. That ignored commission and
slippage already settled into `TradeRecord.PnLPercent`. Entry, exit, and equity timestamps were
also truncated to dates, erasing ordering and holding duration for intraday backtests.

The dormant regime model compounded or summed per-trade percentages and labelled them as total
return and maximum drawdown. Those values were not portfolio returns because regime groups are
discontinuous and trades use different capital allocations. Missing benchmark history was silently
classified as a bull market.

## Decision

- `BacktestResponse` and its nested response records are the sole HTTP result contract.
- `BacktestEndpoints` only handles validation/status mapping and delegates projection to
  `BacktestResponse.Create`.
- The desktop imports `BacktestRequest` and `BacktestResponse` from generated OpenAPI types. The
  stale manual result interface and unused list/get placeholders are removed.
- Every already-calculated headline risk metric, excursion metric, survivorship warning, and regime
  breakdown crosses the contract without changing its established unit.
- Trade `ReturnPct` is the settled, cost-adjusted `PnLPercent`; `NetPnL` makes the currency result
  explicit. Trade and equity timestamps use round-trip ISO format.
- Regime summaries report trade count, win rate, additive net PnL, average completed-trade return,
  and profit factor. They do not claim a portfolio return or drawdown. A market regime is emitted
  only when same-day or prior benchmark evidence exists; year groups remain available independently.
- The desktop explains risk metrics in investor language, preserves technical names as subtitles,
  and shows time for intraday fills.

## Consequences

The backtest response now has a stable generated schema and previously unreachable result evidence
is visible. Trade return can differ from the raw price move because it correctly includes execution
costs. Intraday timestamps retain time-of-day, and unknown benchmark history no longer appears as a
bull market. These are intentional result-presentation corrections; strategy execution, portfolio
equity, and database schema are unchanged.
