# ADR 0065: Unify compiled position instructions

## Status

Accepted

## Context

Preview, backtest, and live execution already shared the compiled strategy runtime and the long
position execution session. However, each path independently converted custom close and scaling
conditions into `StrategyExitInstruction` and `LongPositionScalingInstruction` values. The copies
calculated profit percentage, passed execution counts, mapped rule fields, and chose exit reasons
separately.

The live copy used a strategy-name-specific Korean reason while preview and backtest used the
canonical execution reason. Future changes to scaling evaluation or instruction fields could also
reach only one copy even though every path ultimately called the same execution session.

## Decision

- `CompiledStrategyPositionInstructionResolver` in `Application/Execution` is the sole owner of
  compiled custom close-rule and scaling-rule instruction creation.
- The resolver accepts one fresh `ICompiledStrategyRuntime`, a causal prepared bar window, the
  execution and entry prices, persisted scaling counts, and an adapter-supplied maximum position
  cost. It owns condition dispatch, profit-percentage calculation, rule mapping, and the canonical
  custom-exit reason.
- Preview supplies an unlimited visual capacity because it has no portfolio ledger. Backtest and
  live adapters retain the central portfolio-cap calculation and pass its result to the resolver.
- Backtest retains a thin wrapper solely to select the bounded causal window and strategy runtime.
  Live retains asynchronous reference-data preparation. Neither adapter evaluates custom rules.

## Consequences

The same compiled condition now produces the same execution-session instruction in preview,
backtest, and live operation. Existing fill priority, quantities, capital caps, execution counts,
and historical results do not change. Live custom-rule exits now record the canonical `청산 규칙
충족` reason used by research instead of embedding the strategy name; this intentional operational
label correction is protected by a characterization test and release note.
