# ADR 0011: Prepare one optimization evaluation context

## Status

Accepted

## Context

Synchronous optimization and the persistent worker independently selected a data feed, chose a
market-regime benchmark, compiled reference symbols, expanded requested timeframes, loaded prepared
bars, and copied configured risk limits. This left both adapters coupled to provider, detector,
configuration, and backtest preparation services even after candidate execution was shared.

The duplicated benchmark decision also inspected the nullable request instead of the data source
actually selected from user settings. When a request omitted `DataSource` and the preferred source
was LS Securities, the application selected the Korean feed but still requested US `SPY` regime
data.

## Decision

`IOptimizationEvaluationContextPreparer` is the application port that turns an `OptimizeRequest`
into either a complete `OptimizationEvaluationContext` or a typed preparation failure. Its
implementation owns feed selection, benchmark regime preparation, reference-symbol collection,
timeframe expansion, prepared market data, and configured optimization risk inputs.

`IDataFeedServiceFactory.SelectAsync` returns both the resolved source identity and service. The
central `MarketRegimeBenchmarkPolicy` maps LS Securities to `069500` and US providers to `SPY`.
Ordinary backtests and optimization use this same policy and the resolved source rather than the
nullable request field.

The synchronous adapter converts preparation failure to an empty optimization response as before.
The persistent worker converts it to a failed job as before. Neither adapter owns market-data
selection rules.

## Consequences

- Synchronous and background optimization load identical symbols, timeframes, regimes, and risk
  settings.
- A preferred LS data source uses the Korean regime benchmark even when the request omits a source.
- The job executor depends on application ports and persistence rather than concrete data services.
- Provider failures remain observable through the existing adapter-specific response behavior.
