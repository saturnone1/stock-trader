# Use one evidence-based market regime

- Pattern preview, backtest, live scanning, stock analysis, and the ML fallback now use the same
  completed-bar 200-day benchmark trend policy.
- Benchmark bars after the evaluation time are ignored, preventing future market data from changing
  a historical regime decision.
- Fewer than 200 completed benchmark bars now produce `알 수 없음` and fail closed. Previously,
  preview and some backtest fallbacks silently assumed a bull market and could allow bull-only
  entries without enough evidence.
- Preview and backtest results now warn when this conservative insufficient-history rule affects
  the requested period.
- A close exactly on the 200-day average is consistently bearish rather than bullish only in the
  preview.
- `MarketRegimeTrendPolicyTests` characterize the intentional historical-result correction,
  cutoff safety, equality boundary, live parity, and backtest fallback behavior.
