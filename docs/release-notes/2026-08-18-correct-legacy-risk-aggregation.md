# Correct legacy multi-account risk aggregation

- Corrected portfolio risk PnL for legacy open positions whose `AccountId` is `0`. They are now
  assigned to the first enabled account exactly once instead of being repeated for every enabled
  account.
- The old result was wrong because one economic position contributed the same unrealized PnL once
  per account, overstating gains or losses and potentially changing the daily-loss halt decision.
- Risk overview holding days now use one injected observation time and clamp future-dated positions
  to zero days instead of displaying a negative duration.
- Risk-monitor retry cooldown and repeated halt-alert intervals are now validated operational
  settings rather than worker constants.
