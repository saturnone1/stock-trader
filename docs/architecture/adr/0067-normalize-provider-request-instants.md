# ADR 0067: Normalize historical provider request instants

## Status

Accepted

## Context

Historical market-data callers use UTC instants for intraday ranges and UTC date boundaries for
date-only research ranges. Alpaca forwarded those `DateTime` values directly to its SDK. Yahoo
instead replaced every input's `Kind` with `Local` before creating Unix timestamps.

Replacing `Kind` does not convert an instant; it reinterprets the same clock fields in the host time
zone. The desktop already converts an Eastern intraday selection to UTC. In the production
New York container, Yahoo therefore shifted a summer request four hours later and could omit most
of the selected morning session. A developer machine in another time zone produced a different
request from the same API payload.

## Decision

- `MarketDataRequestWindowPolicy` in `Application/MarketData` is the owner of historical provider
  interval normalization.
- UTC inputs remain unchanged. Local inputs are converted to UTC. Unspecified date values follow
  the application's existing UTC interval convention by receiving `DateTimeKind.Utc` without a
  host-time-zone conversion.
- Empty or reversed intervals are rejected before constructing a provider request.
- Alpaca and Yahoo both pass the normalized UTC boundaries to their protocol adapters. Neither may
  reinterpret a caller's UTC instant through the host local time zone.

## Consequences

The same historical request now produces the same provider boundaries on Windows development,
Linux CI, and the K3s container. Alpaca's intended UTC request semantics become explicit. Yahoo
intraday requests now preserve the exact range selected in the preview instead of shifting it by
the host offset. This intentional Yahoo data-range correction is protected by an exact Unix-window
characterization test and documented in the release notes.
