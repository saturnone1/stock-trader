# ADR 0046: Centralize signal actionability by observation time

## Status

Accepted

## Context

Persisted pattern signals were marked active when detected but were never deactivated. Signal
browsing and dashboard counts therefore treated every non-superseded historical row as currently
actionable. Manual entry had a separate hardcoded 24-hour check, performed only after recommendation
evaluation, and accepted timestamps in the future. These paths could disagree about whether the
same signal was eligible for action.

`SignalBarAt` identifies the market-data event and can use provider-specific daily-bar timestamps.
It is not an application observation clock. `DetectedAt`, stamped from the injected `TimeProvider`,
is therefore the appropriate value for operational lifetime.

## Decision

- `SignalFreshnessPolicy` is the single deterministic owner of the closed actionability window and
  future-timestamp rejection.
- The operational lifetime is validated typed configuration under
  `SignalLifecycle:ActionableLifetimeHours`; the default deployment value is 24 hours and the
  fail-closed upper bound is seven days.
- Signal browsing and dashboard composition obtain one observation instant from `TimeProvider`,
  derive the policy window once, and pass explicit inclusive bounds to persistence adapters.
- The pattern-signal cache stores only the raw active/non-superseded set and applies the requested
  window on every read, so a cached row cannot remain actionable beyond its deadline.
- Manual entry evaluates market availability, freshness, and signal price geometry before running
  recommendation sizing or contacting an account or broker. Future-dated and expired signals fail
  closed with distinct operator messages.
- Historical rows remain unchanged for audit. `IsActive` continues to record the detector's stored
  state; application views use the stricter actionable definition.

## Consequences

Signal lists, dashboard counts, and manual execution now agree at the same observation boundary.
The lifetime can be changed operationally without editing trading code, while invalid or excessively
large values stop startup. This policy does not infer exchange holidays or bar completion from wall
clock time; those remain explicit market-data and calendar concerns.
