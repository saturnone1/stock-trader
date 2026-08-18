# ADR 0049: Separate broker snapshots from durable positions and clock streaming status

## Status

Accepted

## Context

`IBrokerService.GetPositionsAsync` returned the EF-backed `Position` type even though Alpaca and LS
balance responses contain only a current holding snapshot. Both adapters filled the required
`OpenedAt` property with `DateTime.UtcNow`. A caller could therefore interpret every balance refresh
as a newly opened trade and derive a false holding period. `OrderService` retained an unexposed
broker-first position read that returned these incomplete entities as if they were durable strategy
positions.

The codebase also carried two nearly identical Alpaca trading adapters, although only the
account-scoped adapter was constructed. Streaming health, bar flushing, watchlist synchronization,
and reconnect delays read the system clock or embedded operational intervals. Several Alpaca
configuration fields were displayed in `appsettings.json` but had no consumer, so changing them
could not affect the SDK endpoint or subscription behavior.

## Decision

- Broker balance reads return `BrokerPositionSnapshot`, which contains only symbol, quantity,
  average entry price, and current price. It has no strategy state or invented open timestamp.
- Durable `Position` creation remains behind `LiveEntryPositionFactory` and requires confirmed
  order fill quantity, price, and fill time. The unused broker-first `IOrderService` position read
  is removed.
- The unused options-based `AlpacaBrokerService` is removed. `DynamicAlpacaBrokerService` is the
  single Alpaca trading adapter created by `AccountBrokerServiceFactory`.
- Alpaca and LS trading adapters receive the application `TimeProvider` for observation timestamps.
  Missing Alpaca SDK submission time uses that explicit observation. LS order history converts its
  `OrdDt`/`OrdTime` evidence from KST to UTC and excludes rows whose actual order time cannot be
  established. `BrokerAccount` no longer creates its own wall-clock default.
- Streaming activity owns its observation time and staleness decision through `TimeProvider`.
  Reconnect, staleness, flush, synchronization, and buffer values live in one validated
  `StreamingSettings` section.
- Unused Alpaca URL and stream-type configuration is removed rather than presented as an effective
  control.
- The LS `CSPAQ13700` history request uses its documented account endpoint (`/stock/accno`) rather
  than the order-submission endpoint.
- LS OAuth expiry, safety margin, and chart-request pacing use a deterministic timing policy and
  injected `TimeProvider`. The documented 07:00 KST expiry and one-chart-request-per-second limit
  are named protocol constants; the renewal safety margin is startup-validated configuration. The
  LS data feed uses the same clock when choosing its current Korean-market boundary.

## Consequences

Broker holdings can no longer masquerade as persisted strategy positions or reset their apparent
holding age on every query. Live entries still require broker order evidence and preserve the same
fill, stop, target, and quantity semantics. LS reconciliation cannot make an old order look newly
submitted merely because history was queried now. Streaming health and timers can be exercised
with a controlled clock, and invalid operational values fail at startup. No database or public
HTTP contract changes are required.
