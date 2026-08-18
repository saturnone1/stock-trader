# ADR 0019: Centralize built-in pattern detectors

## Status

Accepted

## Context

Dependency injection registered built-in pattern detectors one by one while `BacktestService`
contained a second constructor list for parameter overrides. The two lists could drift. They did:
`Tqqq200SmaDetector` existed with a public pattern type and execution policies but appeared in
neither list, so selecting that strategy could never create its detector in live scanning or
backtesting.

## Decision

`BuiltInPatternDetectorCatalog` is the single mapping from every non-custom `PatternType` to its
detector implementation. `PatternServiceExtensions` registers runtime detectors by iterating this
catalog. `BacktestService` asks `IBuiltInPatternDetectorFactory` to create the same catalog using
either baseline settings or the request's merged parameter overrides.

The factory supplies one fixed options snapshot to each detector while resolving any additional
scoped dependencies through the normal service provider. It verifies that the constructed
detector's reported pattern type matches its catalog entry.

## Consequences

- Live scanning, analysis, ordinary backtests, walk-forward runs, and optimization see the same
  built-in strategy inventory.
- TQQQ 200-SMA is now constructible in both runtime and research paths.
- Adding a `PatternType` without a catalog entry fails the enum-coverage golden test.
- Registration and parameter-override construction cannot maintain divergent detector lists.
