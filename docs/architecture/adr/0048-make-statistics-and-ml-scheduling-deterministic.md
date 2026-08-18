# ADR 0048: Make statistics timestamps and ML scheduling deterministic

## Status

Accepted

## Context

The stateless backtest performance calculator stamped each derived `PatternStats` row with
`DateTime.UtcNow`. Replaying the same strategy document, market data, and date range therefore
returned different JSON metadata. Operational pattern-statistics calculation stamped a second wall
clock, while its SQLite repository overwrote that timestamp with a third observation during save.

The ML retraining worker also read `DateTime.UtcNow` directly and owned its 17:00 ET window, retry,
failure, and cooldown values as private constants. Its recurring `PeriodicTimer` advanced by elapsed
24-hour periods. After the autumn DST transition, a 17:00 EDT timer could recur at 16:00 EST and be
rejected by the worker every day, preventing all subsequent automatic retraining.

## Decision

- Backtest pattern and strategy statistics receive the request's explicit `To` boundary as their
  calculation timestamp. Pure calculators do not read an observation clock.
- `StatisticsService` receives `TimeProvider`, samples one UTC instant for a complete refresh batch,
  and owns every resulting `LastUpdated` value. `PatternStatsRepository` persists that value without
  replacing it and no longer exposes its unused single-row save path.
- Pattern-statistics cache duration is validated typed configuration rather than a private constant.
- `MlRetrainingSchedulePolicy` is a pure application policy that evaluates weekday/ET eligibility
  and calculates initial and recurring delays from explicit observations and a supplied time zone.
- `MLRetrainingService` uses the injected `TimeProvider` for observation and cancellable delays.
  Recurring delays are re-anchored to the first eligible ET window after the configured interval,
  so weekends and either DST transition cannot strand the worker before the daily window.
- ML directory, training, blend, interval, ET window, retry, consecutive-failure, and cooldown
  settings are startup-validated. The deployment values live only in `appsettings.json`.

## Consequences

The same backtest inputs now produce the same statistics timestamps and payload. Operational refresh
rows in one batch share a verifiable observation instant. Automatic ML training remains outside
market hours across time-zone transitions and can be replayed in unit tests without sleeping or
reading the host clock. Return, drawdown, win-rate, and model-training formulas are unchanged.
