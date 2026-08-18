# ADR 0039: Isolate signal and pattern-statistics queries

## Status

Accepted

## Context

`SignalEndpoints` loaded signal and pattern-stat entities, filtered and sorted them, calculated R/R,
and built a dictionary keyed only by pattern type. The database permits aggregate and symbol-level
rows for the same pattern, so that dictionary threw on valid duplicate pattern keys. Even without a
duplicate, it could attach another symbol's win rate to a signal. `PatternStatsEndpoints` also read
the broad persistence repository directly. Expectancy and profit-factor formulas were duplicated on
the EF model and new application projections, and the old profit-factor formula could divide by zero
for a perfect win rate with a retained average-loss value.

## Decision

- `IPatternStatisticsQuery` projects storage-independent pattern statistics and owns deterministic
  expectancy ranking.
- `PatternStatisticsMetricPolicy` in Domain is the sole expectancy and profit-factor formula owner.
  A non-positive gross-loss contribution produces profit factor zero instead of division by zero.
- `ISignalListQuery` composes active signals with statistics. `SignalListPolicy` owns pattern/search
  filtering, R/R calculation, stable sorting, and statistic selection.
- `PatternStatisticsSelectionPolicy` is shared by signal browsing and live ML confidence scoring.
  Statistic selection prefers the exact symbol and pattern, then the aggregate pattern row. Rows for
  other symbols are never attached and multiple valid rows cannot cause a duplicate-key exception.
- Signal and pattern-stat HTTP adapters map explicit response contracts and do not import persistence.
- Portfolio performance reuses the same statistics query and API statistic response contract.
- Desktop consumers use generated camel-case contracts and no longer mask schema drift with
  PascalCase fallbacks.

## Consequences

Signal ranking and displayed historical confidence are deterministic and symbol-correct. The
same selection rule now supplies the historical win rate used by live ML confidence scoring. The
pattern-statistics screen renders the actual API fields. A perfect-win-rate row with inconsistent
legacy loss data no longer fails the request. Architecture and characterization tests prevent
formula duplication, endpoint persistence access, and legacy response casing from returning.
