# Correct daily-report boundaries

- Daily reports now select completed trades by exit time. A position opened before the report day
  and closed during it is no longer omitted.
- Signal totals cover the full report day instead of only the latest 50 recommendations.
- US market-day boundaries convert both local midnights independently, so daylight-saving days use
  their real 23- or 25-hour UTC span.
- Report scheduling, PnL percentage calculation, top-signal ordering, and symbol deduplication are
  deterministic application policies driven by one injected observation time.
- If active broker equity is unavailable, the documented entry-value fallback remains in effect.
