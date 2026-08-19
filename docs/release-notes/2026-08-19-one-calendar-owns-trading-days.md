# One calendar owns every trading-day answer

Four scheduling and cooldown policies still decided trading days by day of week after the execution
calendar had already moved to exchange holiday evidence. They now all consult the same
`ExchangeCalendarCatalog`, so the system can no longer hold two different opinions about whether a
given date was a trading day.

- **Daily market-data sync** treated every weekday as a sync target and used the regular close to
  judge whether the day's bar was complete. It now skips exchange holidays and, on an early-close
  day, judges completeness against that day's actual close rather than waiting for the regular one.
- **Daily report scheduling** skipped only weekends, so a holiday produced a report covering a day
  with no trading. It now advances to the next real trading day.
- **ML retraining** treated holidays as eligible retraining days even though no new completed trades
  existed. `MlRetrainingWindowStatus.Weekend` is renamed `NonTradingDay` to match what it now means.
- **Strategy re-entry and consecutive-loss cooldowns** counted weekdays. A cooldown expressed in bars
  therefore expired early whenever a holiday fell inside it, releasing a block that should still have
  applied. Cooldowns now count actual trading days.

The two directions of failure are deliberately different. Order placement fails closed: an unknown
calendar date reports the market as closed. Scheduling fails open: an unknown date counts as a
trading day, so reports, retraining, and cooldown expiry are never deferred indefinitely. These
paths do not place orders, and the closed-market gate still applies to anything that does.
`MarketCalendarSchedulingExtensions` is the single place that encodes the scheduling direction, and
an architecture test asserts none of the five policies decides trading days by `DayOfWeek` again.
