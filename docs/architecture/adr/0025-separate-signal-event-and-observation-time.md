# ADR 0025: Separate signal event and observation time

## Status

Accepted

## Context

Pattern detectors stamped `PatternSignal.DetectedAt` with the system clock. Replaying the same bar
therefore produced a different signal identity, while the persistence uniqueness key used that
changing value. Restarts could save the same market event again, and all named custom strategies
shared the `Custom` pattern type. Replacing `DetectedAt` with the bar timestamp alone was unsafe
because live order review uses it as the observation age.

## Decision

- Detectors are deterministic and stamp both their research result and `SignalBarAt` from the
  evaluated OHLCV bar.
- The live detection application boundary overwrites only `DetectedAt` with its injected clock.
- New persisted signals require `SignalBarAt` and are idempotent by symbol, pattern, custom strategy
  name, and bar time.
- EF filtered indexes enforce separate built-in and named-custom identities. Legacy rows retain a
  null bar time rather than receiving an invented historical value.
- Custom strategy evaluation no longer depends on a system clock.

## Consequences

Preview, backtest, and optimization replay the same market event deterministically. Live freshness
checks continue to use actual observation time, while restarts do not create another signal for the
same strategy and bar. The migration preserves all legacy signal rows.
