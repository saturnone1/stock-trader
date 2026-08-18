# ADR 0040: Isolate daily-report scheduling and generation

## Status

Accepted

## Context

`DailyReportService` combined its hosted loop with settings and database access, broker calls,
ET/KST conversion, report-day construction, PnL calculations, response projection, and notification
dispatch. It sampled `DateTime.UtcNow` repeatedly and formed a market day by adding 24 hours to its
UTC start. A local day is 23 or 25 hours at a daylight-saving transition. Completed trades were
also read through the generic history filter, whose lower boundary applies to entry time; a trade
opened earlier but closed on the report day was therefore omitted. Recommendations were truncated
to the latest 50 before the report counted the day's signals.

## Decision

- `DailyReportPolicy` is the deterministic owner of next-run scheduling, market-local half-open day
  windows, PnL percentage denominators, top-signal ranking, and executed-symbol projection.
- Both local midnights are converted independently to UTC, preserving 23- and 25-hour market days.
- `IDailyReportActivityStore` returns storage-independent completed-trade and recommendation
  snapshots for an exact `[fromUtc, toUtc)` window. Its SQLite adapter filters completed trades by
  exit time and does not impose an arbitrary recommendation limit.
- `IActiveAccountEquityReader` and `IDailyReportPublisher` isolate broker and notification adapters.
- `DailyReportGenerator` captures one observation from `TimeProvider`, reads activity and equity,
  builds one report, and publishes it.
- The hosted service owns only the cancellable schedule loop and scoped use-case resolution. Its
  fallback and retry times are validated typed options.

## Consequences

Daily reports include every signal and every trade completed during the actual US market-local
calendar day, including positions entered earlier. Historical report totals can therefore increase;
the former result was incomplete. DST transitions no longer shift a report window by one hour.
The same pure policy can be tested without starting a worker, broker, database, or notification
channel, and architecture tests prevent report arithmetic and persistence dependencies from moving
back into the hosted adapter.
