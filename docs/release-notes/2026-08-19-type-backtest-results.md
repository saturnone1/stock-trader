# Type and complete backtest results

- `/api/backtest` now returns an explicit generated contract instead of an anonymous response.
- The desktop now displays the already-calculated CAGR, Sortino, Calmar, profit factor, Half-Kelly,
  MAE/MFE, survivorship warning, and regime/year breakdowns.
- Trade history return is now the settled net return after slippage and commission. The previous
  endpoint recomputed a gross price move and could show a profitable trade even when costs made its
  net result negative.
- Intraday trade and equity timestamps retain time-of-day instead of collapsing every point to the
  same date.
- Regime summaries no longer label summed trade percentages and hypothetical grouped drawdown as
  portfolio results. They show net PnL, average completed-trade return, and profit factor.
- Missing or pre-history benchmark observations are no longer invented as a bull regime.
- `Create_ExposesRiskMetricsAndCostAdjustedTradeReturnWithIntradayTime` and
  `ComputeRegimeStats_DoesNotInventBullRegimeWithoutPriorBenchmarkEvidence` characterize the
  intentional presentation corrections.
