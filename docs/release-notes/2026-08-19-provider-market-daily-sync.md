# Provider-market daily synchronization

- Split daily history synchronization from its background scheduler into a directly testable cycle
  and provider-bound data session.
- Added one typed market catalog for provider ownership, display names, time zones, and regular
  session boundaries while preserving existing API labels.
- Corrected cross-market scheduling: Alpaca/Yahoo now wait for the US close window and LS Securities
  waits for the Korean close window.
- Prevented initial recovery from storing the current unfinished daily bar.
- Corrected OHLCV replacement semantics. Scheduled synchronization re-fetches the last stored date,
  and SQLite upsert replaces an earlier partial sample with the completed provider value.
- Live daily scan deduplication now includes the effective provider and its market-local date.
- Added market-window, provider-switch, partial-retry, completed-bar, SQLite replacement, settings,
  and architecture regression coverage.

This correctness fix can change later research or live decisions when an old database contains an
incomplete bar that was previously frozen. The next successful post-close synchronization repairs
that bar from provider data; the pre-deployment SQLite backup remains the rollback boundary.
