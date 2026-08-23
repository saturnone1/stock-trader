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

The engine now contains 18 owned or linked C# files, 905 nonblank source lines, and no direct
dependency. Its largest file is `StrategyRuleModels.cs` at 156 nonblank lines; the compiler is 136.
Indicator mathematics is split into files of 115 and 106 nonblank lines instead of one 258-line
service. The F# shadow host remains 55 physical lines. Duplicated artifact compatibility and
indicator formula lines are zero because both the application and worker consume shared assemblies.

## Verification and next gate

Architecture tests enforce the dependency edges, persistence/rule-model split, worker references,
shared artifact policy, formula-free application adapter, and storage-free engine price bars.
Indicator parity tests compare every adapter result with the independent engine result. Existing
strategy compiler, preview, backtest, optimization, and live characterization tests must remain
unchanged and green.

This ADR does not claim the complete deterministic engine has been extracted. Before remote
optimization computation is enabled, indicator evaluation, fills, portfolio simulation, result
metrics, and prepared-data contracts must also compile into package-bounded engine assemblies and
pass the shared semantic conformance corpus.

## Rollback

Restore the excluded sources to the web project and remove its project references. The change has no
database, API, result-history, Kubernetes, or financial-writer migration.
