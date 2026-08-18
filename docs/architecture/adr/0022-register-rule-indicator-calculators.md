# ADR 0022: Register rule-indicator calculators

## Status

Accepted

## Context

`IndicatorCatalog` owned UI metadata, defaults, validation, and warmup requirements, while a
649-line `RuleIndicatorEvaluator` independently listed all calculation codes in one switch. Adding
an indicator therefore required coordinated edits in two distant inventories, and a missing runtime
implementation silently returned a neutral value that could change trading decisions without an
error.

The evaluator also combined dispatch, per-symbol caching, common indicator-service calls, price
structure, momentum, volume, and hand-written mathematical primitives in one file.

## Decision

- `RuleIndicatorCalculatorRegistry` is the single runtime mapping from indicator code to calculator.
- Registry construction compares its codes with every `IndicatorCatalog` descriptor and fails on a
  missing, unknown, or duplicate implementation.
- `RuleIndicatorEvaluationContext` owns one-symbol evaluation data and calculation caching.
- Standard, price-structure, and momentum/volume calculators live in bounded category components.
- `RuleIndicatorMath` owns deterministic primitives not supplied by `IIndicatorService`.
- `RuleIndicatorEvaluator` only resolves bar offsets, preserves the legacy insufficient-history and
  unknown-code behavior, and delegates the calculation.

## Consequences

The orchestration boundary is 43 lines instead of 649, and no calculator component exceeds 230
lines. A catalog entry can no longer reach the strategy builder without an executable calculation.
Preview, backtest, optimization, scanning, and live exits continue to use the same evaluator through
the compiled custom-strategy runtime, with existing parity and rule-evaluation goldens protecting
the numerical contract.
