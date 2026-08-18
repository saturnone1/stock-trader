# Deterministic statistics clocks and ML schedule

- Backtest pattern/strategy statistics now use the requested result end boundary instead of the
  machine clock, making identical replays return identical metadata.
- Live pattern-statistics refreshes use one injected observation instant and SQLite preserves it.
- Pattern-statistics cache duration and all automatic ML retraining operational values are validated
  configuration rather than worker/service constants.
- Automatic ML retraining now recalculates its ET eligibility after each configured interval,
  preventing the autumn DST transition from leaving the worker permanently before its daily window.
- Trading returns, fills, costs, drawdowns, and model-training formulas are unchanged.
