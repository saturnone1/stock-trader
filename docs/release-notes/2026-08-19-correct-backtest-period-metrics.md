# Correct backtest period metrics

- Backtest CAGR now uses the full requested evaluation period instead of the interval from the
  first entry to the last exit.
- Calmar uses that corrected annualized return and the portfolio maximum-drawdown fraction through
  one unit-explicit policy.
- Headline Sharpe and Sortino now annualize completed-trade frequency over the same full evaluation
  period instead of only the interval containing trades.
- The old result was wrong because idle time before the first trade or after the last trade was
  excluded, which could overstate annualized performance without changing portfolio profit.
- The public `AnnualizedReturn` contract remains in percentage points (`10 = 10%`); total return
  and drawdown remain fractions (`0.10 = 10%`).
- Sub-day tests retain a one-calendar-day annualization floor, and numerically explosive CAGR is
  capped at one million percent instead of failing the entire backtest with decimal overflow.
- The named `Evaluate_UsesTheFullRequestedPeriod_NotTheActiveTradeWindow` characterization test
  documents this intentional historical-result correction.
