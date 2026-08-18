# Correct portfolio maximum drawdown

- Portfolio maximum drawdown now starts from the configured account size and applies realized PnL
  in deterministic exit-time and trade-ID order.
- The old result was wrong because its peak started at zero cumulative profit. Losses that occurred
  before the account first became profitable were reported as 0% drawdown.
- Portfolio performance now reads the complete persisted trade history. The previous query silently
  inherited a 1,000-row page limit and could omit older trades.
- The named `Evaluate_UsesInitialAccountEquityAndRecognizesAnOpeningLoss` characterization test
  documents the intentional historical-result change.
- Portfolio, trade, and dashboard position lists now share one observation time and one durable
  order-status projection instead of independently reading and formatting position entities.
- Portfolio performance avoids concurrent operations on the scoped SQLite context when both
  settings and pattern-stat caches are cold.
