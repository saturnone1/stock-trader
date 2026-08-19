# ADR 0068: State market-data evidence explicitly

## Status

Accepted

## Context

Preparation and execution depended on several facts that no input ever stated.

Price adjustment was decided inside each provider adapter and never recorded. Alpaca requested
`Adjustment.SplitsAndDividends`. Yahoo rescaled OHLC by its own `adjclose` ratio. LS Securities
requested `sujung="Y"` for daily and weekly bars but sent no adjustment field at all on the minute
TR, so the same provider returned adjusted daily bars and unadjusted intraday bars. A stored result
carried no field distinguishing these, so two results could not be shown to be comparable.

Trading-day decisions used a weekend-only rule. `MarketCalendar.IsMarketOpen` treated every weekday
as a session day, and its own comment acknowledged that holidays needed separate data. That rule
reported Thanksgiving, Christmas, and Korean substitute holidays as open, and treated the afternoon
of a 13:00 ET early-close day as regular session time.

Warmup was calendar-day arithmetic with fixed padding — `Tqqq200SmaExecutionPolicy` added a
30-day "holiday and feed buffer" precisely because no real calendar existed. Session scope, provider
market, and market time zone were likewise implicit in whichever adapter happened to serve a request.

## Decision

- `PriceAdjustmentCatalog` owns the adjustment mode each provider delivers, keyed by provider and
  time frame so LS Securities' per-timeframe divergence is stated rather than hidden. Characterization
  tests bind the catalog to what the adapters actually request.
- `ExchangeCalendarCatalog` owns exchange holiday and early-close evidence for the US and Korean
  markets, with an explicit coverage range and a `MarketCalendarVersion`. Dates outside coverage
  raise `MarketCalendarCoverageException` instead of being answered by a weekday guess.
- `MarketCalendar` delegates trading-day status to that catalog and uses each day's actual close
  time. The live gate fails closed: a coverage gap logs an error and reports the market as closed
  rather than propagating an exception into a background worker, while the analytical
  `GetTradingDay` surface fails loudly so research cannot silently proceed on unknown ground.
- `MarketDataEvidence` is the explicit statement of one run's conditions — provider, market region,
  market time zone, time frame, adjustment mode, session scope, calendar version, warmup calendar
  days, and required warmup bars. `IDataFeedService` exposes its `Source` so preparation can
  assemble that evidence from the catalogs rather than from a caller's assumption.
- `PreparedBacktestData` carries the evidence, `Slice` preserves it, and it reaches `BacktestResult`
  and the `BacktestResponse` contract. Optimization keeps evidence per time frame, because a run
  that selected a different time frame also selected a possibly different adjustment mode.

## Consequences

A stored backtest result now states the conditions that produced it, and `IsComparableTo` makes the
comparability question answerable instead of assumed. The LS Securities daily/intraday adjustment
split becomes visible in results rather than being an undocumented property of whichever code path
served the bars.

Holiday and early-close corrections intentionally change historical behavior: sessions that the old
rule reported as open are now correctly closed. `MarketCalendarTests` and `ExchangeCalendarCatalogTests`
characterize each corrected case, and the change is documented in the release notes.

The calendar carries evidence for 2024-2027. Extending it is a deliberate act that also advances
`MarketCalendarVersion`, so results remain attributable to a known calendar. Running past the
coverage end causes the live gate to refuse trading until the calendar is extended — a visible,
recoverable stop rather than a silent wrong answer.

The remaining weekend-only checks in `DailyMarketDataSyncPolicy`, `DailyReportPolicy`,
`MlRetrainingSchedulePolicy`, and `StrategyTradeTransitionPolicy` are not yet migrated. They schedule
operational work rather than deciding execution semantics, so they are lower risk, but they should
adopt the same catalog so that one calendar owns every trading-day answer.
