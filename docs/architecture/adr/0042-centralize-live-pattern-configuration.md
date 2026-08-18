# ADR 0042: Centralize executable pattern inventory and live configuration

## Status

Accepted

## Context

The central pattern catalog described every non-custom enum value as a supported built-in strategy.
However, opening-range breakout and earnings drift had detector classes whose only behavior was to
return no signal. Settings and symbol profiles accepted them, and a direct backtest silently
returned an empty result. A zero-trade result was therefore indistinguishable from a valid strategy
that found no opportunity.

Live parameter promotion also had two runtime sources. It saved overrides to the settings database
and then rewrote the deployed `appsettings.json`. Exit evaluation read the database value, while
entry detectors relied on options reloading the modified file. Container restarts, read-only
filesystems, or a failed file write could make entry and exit semantics disagree.

## Decision

- `PatternCatalog` owns operational availability and the investor-facing reason when a built-in
  strategy is unavailable. Stable enum and display identity remain intact.
- The detector inventory contains exactly `PatternCatalog.OperationalBuiltIn`. Always-null detector
  classes are removed.
- Settings, symbol profiles, live promotion, and backtest selection reject unavailable built-ins
  before persistence, provider access, or simulation. Legacy stored selections are read
  compatibly but are not presented as enabled.
- `ILiveParameterService` and its implementation live in `Application/Settings`. They read and
  update `ISettingsManagementStore`; application files are never changed at runtime.
- Live entry detection and live position evaluation read the same persisted override snapshot and
  resolve it through the shared `PatternOverrideMerger`.
- Strategy-builder and settings metadata expose availability explicitly. The Svelte settings page
  keeps unavailable strategies visible, disabled, and explains the missing dependency.
- The live-promotion endpoint binds an explicit generated request and response contract. Risk and
  position limits are required inputs rather than endpoint-owned fallback constants.

## Consequences

Existing database values and pattern enum codes are preserved. Opening-range breakout and earnings
drift can be implemented later without another identity migration, but cannot currently produce a
misleading empty research result or be enabled for live scanning. Applying research settings no
longer requires a writable application image and entry/exit configuration cannot diverge because a
file update failed.
