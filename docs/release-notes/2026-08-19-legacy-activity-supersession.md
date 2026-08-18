# Legacy signal and recommendation duplicate correction

- Pre-identity daily-scanner rows with identical same-day strategy, symbol, and price geometry are
  retained but marked as superseded; only the latest row remains visible to operational readers.
- Executed recommendations, claimed entries, and recommendations with broker evidence are never
  superseded.
- Recommendation lists, dashboard activity, daily reports, signal browsing, live entry counts, and
  manual signal execution now consistently exclude superseded rows.
- This intentionally reduces historical signal/recommendation counts that previously represented
  repeated observations of the same daily setup rather than independent trading opportunities.
- Current `SignalBarAt` and `SourceSignalId` unique identities remain the prevention mechanism for
  all new activity.
