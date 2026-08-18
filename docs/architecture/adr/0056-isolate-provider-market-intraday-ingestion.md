# ADR 0056: Isolate provider-market intraday ingestion

## Status

Accepted

## Context

`MarketDataIngestionService` combined host scheduling with provider selection, settings and OHLCV
repository access, per-symbol latest-bar requests, batch persistence, and scanner publication. Its
market gate opened when either the US or Korean market was open, regardless of the selected data
provider. Alpaca and Yahoo could therefore be polled during the Korean session, and LS Securities
could be polled during the US session.

The streaming fallback gate was also global. An active Alpaca stream suppressed REST polling even
when LS Securities was the selected provider. Retry count, failure threshold, and cooldown were
worker constants, and a total provider outage was swallowed as independent symbol errors, so the
nominal retry and circuit-breaker path never observed that failure.

## Decision

- `IIntradayMarketDataIngestionCycle` is the application-facing use-case contract for one REST
  latest-bar pass.
- `IIntradayMarketDataIngestionData` opens one provider-bound session containing the effective
  provider, normalized watchlist, latest-bar adapter, OHLCV write, and scanner publication.
- The cycle checks only `DataProviderCatalog.Get(session.Source).MarketRegion`. Another market's
  session cannot authorize the selected provider's polling.
- `IRealtimeMarketDataStatus` reports replacement coverage by provider. The Alpaca streaming adapter
  suppresses only Alpaca REST polling. `IRealtimeMarketDataSelectionReader` makes that adapter
  connect only while Alpaca is selected and supplies the normalized watchlist.
- During a provider switch, the old connected stream retains transition ownership until callbacks
  are rejected, in-flight callback processing reaches its lock boundary, the client is disposed, and
  its buffered bars are flushed. REST polling for the new provider waits for that boundary,
  preventing mixed-provider writes.
- Streaming and REST ingestion both publish scanner symbols only after their OHLCV batch commits.
  A failed streaming flush retains its drained batch for the next serialized flush instead of
  silently losing it or allowing the provider handoff to complete. The connection loop explicitly
  cancels and drains its flush loop when reconnect attempts are exhausted.
- `IRealtimeBarIngestionBuffer` owns callback admission, bounded buffering, serialized flushes,
  retained failures, and immediate UI price projection. `IRealtimeBarBatchSink` owns the scoped
  SQLite commit followed by scanner publication. The Alpaca worker is reduced below 400 lines and
  owns only SDK connection, subscription, provider-selection, and reconnect orchestration.
- Successfully fetched bars are persisted in one batch before their symbols are published. A
  per-symbol failure does not discard other samples, while failure of every symbol escapes to the
  worker so configured retry and cooldown behavior is effective.
- `MarketDataIngestionService` owns only the injected-clock timer, scope creation, configured retry
  and cooldown, and application lifecycle logging. Interval and recovery values use validated typed
  options.

## Consequences

REST polling follows the selected provider's market and is no longer affected by an unrelated open
market or stable realtime connection. Provider transitions stop the old stream before polling the
new adapter. A provider-wide outage now activates retry and eventually cooldown
instead of generating a successful-looking cycle.

This intentionally changes live collection timing for deployments whose selected provider did not
match the previously open market or active Alpaca stream. It removes invalid or missing intraday
samples; it does not change the database schema or public HTTP contracts. Provider-market,
provider-stream, persistence-before-publication, partial-success, total-failure, cancellation, typed
settings, and worker-boundary tests characterize the correction.
