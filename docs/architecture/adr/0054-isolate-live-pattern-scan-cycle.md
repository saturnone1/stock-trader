# ADR 0054: Isolate the live pattern scan cycle

## Status

Accepted

## Context

`PatternScannerService` combined channel scheduling and resilience with provider selection, daily
bar access, ET-date deduplication, benchmark-regime caching and calculation, pattern detection,
signal persistence, recommendation evaluation, and order dispatch. This made the trading path hard
to exercise without a hosted worker and let retry/circuit values remain embedded in code.

The worker also marked a symbol as scanned immediately after loading enough bars. Any later exception
was sent to the retry helper, but the retry returned at the daily deduplication check without doing
the failed work. Operationally, a failed detection or signal-processing pass was indistinguishable
from a successful no-signal scan until the next US market date.

## Decision

- `PatternScannerService` owns only channel consumption, scoped cycle invocation, retry, and circuit
  breaker behavior.
- Retry count, consecutive-failure threshold, and cooldown seconds are validated `TradingSettings`.
- `ILivePatternScanCycle` owns one completed-daily-bar scan. Provider context and daily data,
  benchmark-regime evaluation, detection, and signal processing cross purpose-specific ports.
- `LivePatternScanState` owns process-lifetime ET-date deduplication and a benchmark/date keyed regime
  cache. The provider benchmark is part of the cache identity.
- The injected `TimeProvider` supplies one observation instant for the scan, data boundaries, market
  date, and regime timestamp.
- A symbol is marked complete only after a no-signal result or successful signal processing.
  Exceptions leave it eligible for the worker's retry policy.
- Retrying the durable pipeline relies on the existing signal identity, recommendation source-signal
  identity, and entry-execution claim. These remain the financial idempotency boundaries.

## Consequences

The hosted worker contains no market-data, indicator, detection, persistence, recommendation, or
order dependencies. The cycle and regime calculation can be tested directly, including retries that
previously became no-ops. A transient downstream exception can now cause detection to run again;
non-financial notifications may therefore repeat, while persisted signals, recommendations, and
broker-entry claims retain their existing duplicate protection. There is no public HTTP, desktop,
database-schema, or historical strategy-result change.
