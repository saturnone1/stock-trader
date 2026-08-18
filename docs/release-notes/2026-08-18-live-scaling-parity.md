# Live scaling execution parity

- Live daily strategies can now evaluate custom scale-in and scale-out rules with the same compiled
  detector and common long-position execution session used by preview and backtest.
- Per-rule maximum counts come from durable position state, so restarts cannot repeat an exhausted
  scaling rule.
- Scale-in sizing uses the shared portfolio-cap policy with broker account equity, the configured
  maximum position count, and the strategy's single-position percentage limit.
- Missing or invalid broker equity produces zero additional-buy capacity while scale-out remains
  available.
- A same-snapshot protective-stop increase is persisted before any scaling order is submitted.
- Live intent remains unchanged until a direction- and quantity-matched broker fill is reconciled.
