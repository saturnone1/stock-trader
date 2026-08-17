# ADR 0002: Custom-rule evaluation is a deterministic pipeline

Status: Accepted

Date: 2026-08-18

## Context

`RuleBasedDetector` previously compiled strategy documents, calculated every indicator, interpreted
comparison operators, filtered reference-symbol history, combined nested condition groups, selected
initial stop and target prices, and assembled signals. A change to one concern required understanding
more than one thousand lines and made it difficult to prove that preview, backtest, optimization, and
live execution retained identical semantics.

The engine also read `DateTime.UtcNow` while assembling a signal. That made an otherwise identical
evaluation produce a different observation timestamp and hid a system-clock dependency inside the
strategy component.

## Decision

Custom-rule evaluation is an ordered, deterministic pipeline with the following owners:

1. `StrategyCompiler` parses and validates the stored strategy once.
2. `RuleIndicatorEvaluator` owns indicator math and caches values only inside one bar snapshot.
3. `RuleConditionEvaluator` owns required-history checks, reference-symbol as-of filtering,
   fixed or indicator thresholds, consecutive/within-bar semantics, and comparison operators.
4. `RuleGroupEvaluator` owns nested AND/OR combination, matched and total weights, and matched-rule
   explanations.
5. `DynamicExitPricePolicy` owns initial custom-strategy stop and target selection.
6. `RuleBasedDetector` coordinates these components and assembles the signal.
7. `CustomStrategyDetectorFactory` is the only production composition boundary. Consumers depend on
   `ICustomStrategyDetector` and cannot construct or cast to `RuleBasedDetector` directly.

Bar arrays, reference data, the reference as-of boundary, and the observation clock are explicit
inputs. The detector must not read the system clock directly. API, worker, preview, optimization, and
backtest adapters supply the application `TimeProvider`.

These components remain in the existing assembly during the modular-monolith migration. They are
internal and have no EF Core, ASP.NET, HTTP, broker SDK, or configuration dependency.

## Consequences

- Operator and group semantics can be characterized without constructing an entire detector.
- Historical reference evaluation cannot silently see data after its explicit as-of boundary.
- Preview, backtest, optimization, and live paths construct the same detector pipeline.
- Signal observation time can be fixed in tests and replayed deterministically.
- Adding an indicator, comparison operator, group rule, or price-level method has one named owner.
- Preview, backtest, optimization, scanning, and live exits cannot drift into different constructor
  graphs because they all resolve the same factory.
- The detector remains responsible for entry/exit/scaling orchestration until those application
  use cases are separated in a later phase.
