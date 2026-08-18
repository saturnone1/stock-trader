# Live execution session parity

- Live price snapshots now pass through `LongPositionExecutionSessionPolicy`, the same ordered
  position engine used by pattern preview and backtest.
- A live evaluation emits one broker intent at a time and never applies quantity or partial-profit
  state before the broker fill is reconciled.
- Compiled daily/next-open strategies may use the common one-time partial-profit rule in live
  trading. The requested quantity is derived by the shared position-session policy.
- A confirmed strategy partial-profit fill atomically reduces quantity, records the matched trade,
  marks partial profit, and moves the remaining stop to breakeven. Manual partial closes do not.
- Custom scale-in and scale-out remain disabled for live trading until cost basis and per-rule
  execution counters are durable and broker-reconciled.
