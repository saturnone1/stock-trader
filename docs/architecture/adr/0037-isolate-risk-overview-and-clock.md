# ADR 0037: Isolate risk overview projection and observation time

## Status

Accepted

## Context

`RiskEndpoints` read positions and settings directly, calculated risk-per-share, R-multiple, and
holding days, and sampled `DateTime.UtcNow`. `MultiAccountRiskService` and `RiskMonitorService` also
sampled system time directly. Operational retry and alert intervals were embedded in worker code.
This made the HTTP adapter an owner of trading projections and made time-dependent behavior hard to
reproduce. The multi-account fallback additionally included every legacy `AccountId == 0` position
in every enabled account, multiplying portfolio PnL by the number of accounts.

## Decision

- `IRiskOverviewQuery` exposes a storage-independent application projection for the complete risk
  overview. `RiskOverviewQuery` composes risk state, open positions, and user settings behind that
  boundary.
- `PositionRiskProjectionPolicy` deterministically calculates risk distance, R-multiple, and
  non-negative holding days from an explicit observation time.
- Risk services and the monitor receive `TimeProvider`; one evaluation cycle samples one instant.
- `RiskAlertPolicy` owns deterministic halt-alert throttling.
- Monitor failure, cooldown, and halt-alert intervals are validated `TradingSettings` options.
- Legacy accountless positions belong to the first enabled account returned by account management
  for risk aggregation and are counted exactly once. With no enabled account, the portfolio uses
  the existing single fallback aggregation.
- The API maps the application snapshot to explicit response records and owns no risk arithmetic.

## Consequences

Previewing the risk screen, refreshing account risk, and throttling alerts are reproducible under a
fixed clock. Multi-account portfolio PnL no longer overstates accountless legacy positions.
Architecture tests prevent persistence and clock dependencies from returning to the endpoint, and
characterization tests document the intentional historical-result correction.
