# Provider-owned market benchmark

- The central data-provider catalog now owns the market-regime benchmark for every provider:
  `SPY` for US feeds and `069500` for LS Securities.
- Preview, backtest, optimization, live scanning, stock analysis, daily synchronization, and ML
  training resolve that same value after provider fallback.
- Daily synchronization now keeps the benchmark current even when it is not in the watchlist.
- ML training, regime classification, and signal scoring use the injected clock, preserve
  cancellation, and report the configured minimum sample count instead of a hard-coded value.
