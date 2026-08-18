# ADR 0055: Isolate provider-market daily synchronization

## Status

Accepted

## Context

`DailyDataSyncService` combined host scheduling with settings, provider selection, market-time
decisions, SQLite reads and writes, initial history recovery, per-symbol synchronization, and
statistics refresh. Its scheduling condition allowed synchronization after either the US or Korean
market closed, regardless of the selected provider. An Alpaca or Yahoo deployment could therefore
mark its daily sync complete during Korean evening, before the US session had even opened.

Initial recovery could also persist the current unfinished daily bar. `OhlcvRepository` used
`INSERT OR IGNORE`, and scheduled synchronization began one day after the last stored timestamp.
Together those choices froze the first partial sample permanently: the later completed OHLCV for
the same symbol, timeframe, and timestamp was neither requested nor allowed to replace it.

Provider market ownership and regular-session facts were represented by unrelated strings and
hardcoded time-zone/session values in several adapters.

## Decision

- `MarketRegion` and `MarketRegionCatalog` own stable market identity, display name, IANA time-zone
  ID, and regular open/close boundaries in Domain.
- `DataProviderCatalog` owns each provider's typed `MarketRegion`; its existing `Market` display
  property is derived from the market catalog to preserve API output.
- `IMarketCalendar` is an application port. `MarketCalendar` implements it using the domain catalog
  and supplies explicit UTC-to-market-local conversion.
- `DailyDataSyncService` owns only initial invocation, a configured periodic timer, retries, failure
  counting, and cooldown.
- `IDailyMarketDataSyncCycle` opens one provider-bound data session. Its readiness and completion key
  use only that provider's market-local date and configured post-close delay.
- Initial recovery saves only timestamps proven complete for the current market window. Scheduled
  synchronization includes the last stored date rather than starting on the following date.
- OHLCV persistence uses SQLite upsert on `(Symbol, TimeFrame, Timestamp)`, replacing market fields
  when a provider supplies a newer sample for the same canonical bar.
- Live pattern scan completion includes effective provider and provider-owned market date, so an
  intraday provider switch cannot reuse another provider's completion marker.

## Consequences

US and Korean daily data can no longer trigger one another's completion window. A current-session
daily bar is excluded until its provider market has passed the configured close delay. Existing
partial rows are corrected when the overlapping scheduled fetch next returns that bar.

This intentionally can change later preview, backtest, or live results that previously consumed a
frozen partial bar. `AddBarsAsyncReplacesAnEarlierSampleForTheSameBarIdentity` and
`InitialSyncDoesNotPersistTheCurrentUnfinishedDailyBar` characterize why the old result was wrong.
There is no database-schema or public wire-contract change.
