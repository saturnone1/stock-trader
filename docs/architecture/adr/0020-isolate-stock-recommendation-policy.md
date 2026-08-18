# ADR 0020: Isolate stock recommendation policy

## Status

Accepted

## Context

`StockAnalysisService` mixed data-feed access, caching, pattern detection, indicator preparation,
trade-history queries, clock access, and the formulas that produce probability, expected return,
downside risk, stop, target, confidence, and recommendation grade. This made a data-source adapter
the only executable specification for user-visible recommendation numbers. Cache durations,
lookback ranges, and concurrency limits were also private constants.

## Decision

- `StockRecommendationPolicy` is the deterministic owner of recommendation formulas and returns one
  immutable result from explicit market, indicator, pattern, and statistics inputs.
- `StockIndicatorSnapshotFactory` owns the legacy indicator snapshot composition and delegates all
  mathematical primitives to `IIndicatorService`.
- `StockAnalysisService` coordinates data access, cache use, pattern detection, holding-period history,
  and result assembly. It receives observation time from `TimeProvider`.
- Operational cache, lookback, minimum-data, and concurrency values bind to validated
  `StockAnalysisSettings`.
- Characterization tests lock both the no-pattern baseline and a weighted pattern scenario.

## Consequences

Recommendation arithmetic can now be replayed without HTTP, EF, data providers, caches, or system
time. Operational tuning no longer requires source edits. This boundary does not claim that the
statistical model is predictive; it preserves the existing model while making later validation or
replacement explicit and reviewable.
