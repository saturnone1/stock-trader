# ADR 0066: Centralize provider regular-session windows

## Status

Accepted

## Context

The market catalog already owned US and Korean market identity, time zone, and regular-session
boundaries. Alpaca converted those values into UTC inside its adapter, while Yahoo independently
hardcoded the US session as `13:30` through `20:00` UTC. That Yahoo window was correct only during
US daylight-saving time. In winter it requested 08:30 through 15:00 Eastern and omitted the last
regular-session hour.

Duplicating local-to-UTC conversion in provider adapters also allowed future changes in market
hours or time-zone handling to reach one provider without the others.

## Decision

- `RegularMarketSessionWindowPolicy` in `Application/MarketData` is the deterministic owner of
  converting a market-local date, time zone, open, and close into an exact UTC request window.
- Provider adapters obtain market identity from `DataProviderCatalog` and time-zone/session facts
  through `IMarketCalendar`; they do not embed UTC offsets or resolve time-zone IDs themselves.
- Alpaca and Yahoo both use the policy for dated intraday requests. Provider-specific request and
  response protocols remain inside each adapter.
- The policy rejects invalid same-day session definitions before any provider request is made.

## Consequences

Yahoo regular-session requests now follow Eastern daylight-saving transitions: 09:30–16:00 ET is
14:30–21:00 UTC in winter and 13:30–20:00 UTC in summer. The prior winter result was incorrect, so
the correction is protected by exact request-window tests and documented in the release notes.
Alpaca behavior is unchanged but now shares the same conversion owner.
