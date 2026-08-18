# Broker snapshot and streaming clock boundary

- Broker balance reads now return a purpose-specific holding snapshot instead of constructing
  incomplete persisted positions with a fabricated open timestamp.
- Live durable positions continue to be created only from confirmed order fill evidence.
- The duplicate unused Alpaca trading adapter and the unexposed broker-first position read were
  removed.
- Broker account/order observation timestamps and streaming health use the injected application
  clock.
- LS order history uses the response's actual Korean order time and excludes timestamp-less rows
  instead of assigning the query time, preventing false reconciliation candidates.
- The LS `CSPAQ13700` order-history request now uses the documented `/stock/accno` endpoint.
- LS token expiry, renewal safety margin, chart-request pacing, and data-feed observation time now
  use the injected clock. Documented expiry/rate limits are named protocol constants and the safety
  margin remains validated configuration.
- A token issued inside the configured pre-expiry safety window now targets the next KST expiry
  boundary instead of being considered immediately expired and refreshed on every request.
- Reconnect, staleness, flush, watchlist synchronization, and buffer limits are validated streaming
  configuration. Unused Alpaca endpoint and stream-type settings were removed.
- Strategy signals, fills, risk geometry, and database schema are unchanged.
