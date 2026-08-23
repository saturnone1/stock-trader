# 0071 — Extract the deterministic strategy kernel

- Status: Accepted for staged implementation
- Date: 2026-08-23
- Supersedes: none
- Extends: ADR 0069 and ADR 0070

## Context

The Optimization Worker cannot execute or even validate a strategy safely while its compiler,
strategy document, rule models, timeframe identity, and central catalogs are compiled only into the
ASP.NET application. Referencing `StockTrader.csproj` from a worker would create a second executable
around the monolith instead of an independently buildable service. Translating those rules to F#
would create a second trading meaning.

The persistence model also defined `EntryRule`, scaling, exit, and portfolio rule types in the same
source file as the EF-owned `CustomPatternDefinition`. Moving that file wholesale would put a
persistence entity inside the deterministic engine boundary.

## Decision

Create package-free `StockTrader.Engine` as the shared deterministic C# kernel. Its first slice owns
the strategy document, compiler, compiled model, execution rule models, timeframe identity, strategy
and indicator catalogs, document defaults/version policy, live-compatibility policy, and market
calendar version identity. The next slice moves SMA, EMA, RSI, cumulative RSI, Bollinger, VWAP, ATR,
MACD, Keltner, and OBV mathematics into the same assembly over a storage-independent immutable
`PriceBar`.

The following slice moves the complete indicator-code registry, indicator cache, condition
comparison, nested group aggregation, reference-symbol as-of filtering, and warmup policy into the
engine. `RuleBasedDetector` converts application bars and reference series at its boundary, then
delegates rule semantics to the engine.

The execution slice moves long-position bar ordering, conservative stop precedence, entry
repricing, partial exits, target/strategy/time exits, protective-stop advancement, scaling, and
position sizing into the engine. Preview, backtest, and live adapters map `OhlcvBar` through the
single `EnginePriceBarMapper` before invoking the shared session policy.

The cost slice moves exactly-once execution-cost settlement and fixed/adaptive slippage mathematics
into a storage-independent generic engine ledger. The backtest adapter supplies a stable execution
key and projects net PnL and return onto its persistence model; it no longer owns cost formulas.

The portfolio-accounting slice moves realized-equity updates, trading-day loss anchors,
mark-to-market valuation, equity-curve identity, and peak drawdown into a model-independent engine
ledger. The backtest adapter retains position orchestration and maps its open positions and bars to
small value inputs.

`CustomPatternDefinition` remains in the application persistence model. The pure execution rule
types move to the engine project under their compatibility namespace. Legacy compiler and catalog
source paths are linked into the new project and excluded from the web project during this staged
move; the assembly, not the old folder, is now their sole compile owner. Later increments will move
the files physically without changing their public identity.

Create `StockTrader.OptimizationProtocol` as the adapter library that is allowed to depend on both
the engine and transport-neutral service contracts. It is the single owner of strategy artifact
creation and compatibility validation. The engine does not depend on optimization transport types,
and the F# worker does not duplicate validation policy.

The F# shadow worker references the engine, protocol, and service-contract assemblies directly. It
uses the shared compiler when validating a lease but still cannot claim, evaluate, heartbeat, or
submit work. This is not yet a Kubernetes service extraction.

The existing `IndicatorService` remains only as a compatibility adapter from persisted `OhlcvBar`
objects to `PriceBar`; it contains no indicator formulas. This preserves existing application ports
while making the calculation implementation directly reusable by the worker.

## Dependency rules

```text
StockTrader.OptimizationWorker (F#) -> OptimizationProtocol -> Engine
                                     |
                                     +-> ServiceContracts

StockTrader (ASP.NET) -> OptimizationProtocol -> Engine
                     \-> ServiceContracts
```

- `StockTrader.Engine` has no package or project references.
- Engine source cannot import ASP.NET, EF Core, HTTP clients, configuration, broker SDKs, or service
  contracts.
- `StockTrader.OptimizationProtocol` cannot reference the ASP.NET project.
- The web project excludes every source file compiled by the engine to prevent duplicate type
  ownership.

## Agent working-set budget

The engine now contains 40 owned or linked C# files, 2,833 nonblank source lines, and no direct
dependency. Every engine source file remains below 200 physical lines; the largest rule calculator
is 191 lines and the largest physical source is 194 lines. Indicator mathematics is split into files of 115 and 106 nonblank lines instead of one
258-line service, and the standard rule calculator is split into 114- and 119-line files. Execution
contracts, one-bar ordering, entry repricing, and cost settlement remain separate focused files.
The F# shadow host remains 55 physical lines. Duplicated artifact, indicator, comparison, group, and
long-position execution policy lines are zero because all adapters consume shared assemblies.

## Verification and next gate

Architecture tests enforce the dependency edges, persistence/rule-model split, worker references,
shared artifact policy, formula-free application adapter, and storage-free engine price bars.
Indicator parity tests compare every adapter result with the independent engine result. Existing
strategy compiler, preview, backtest, optimization, and live characterization tests must remain
unchanged and green.

This ADR does not claim the complete deterministic engine has been extracted. Before remote
optimization computation is enabled, remaining position orchestration, result metrics, and
prepared-data contracts must also compile into package-bounded engine assemblies and pass the
shared semantic conformance corpus.

## Rollback

Restore the excluded sources to the web project and remove its project references. The change has no
database, API, result-history, Kubernetes, or financial-writer migration.
