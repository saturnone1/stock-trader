# Correct signal statistics resolution

- Signals now select historical statistics for the exact symbol and pattern, falling back only to
  the aggregate row for that pattern.
- Live ML confidence scoring now uses that same selection rule instead of skipping a valid aggregate
  statistic when a symbol-specific row does not exist.
- The old result was wrong because statistics were keyed only by pattern type. Multiple valid
  symbol rows could throw a duplicate-key exception or attach another symbol's win rate.
- R/R sorting now has deterministic observation-time and ID tie breakers.
- Expectancy and profit factor use one domain policy. A zero gross-loss denominator now returns zero
  instead of raising a division-by-zero error.
- The pattern-statistics desktop page now reads the generated camel-case API contract; its prior
  PascalCase-only table fields could render empty cells.
