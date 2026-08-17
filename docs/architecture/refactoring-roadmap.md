# Refactoring roadmap

## Current progress

- Central timeframe, indicator, operator, strategy, and provider catalogs are active.
- Preview, backtest, and live rule evaluation consume the same compiled strategy model.
- Live strategy loading is behind `ICompiledStrategyRepository`.
- Database startup changes run through ordered, versioned migrations.
- Optimization contracts, search-space generation, strategy variants, and ranking are isolated in
  `Application/Optimization`.
- Historical data preparation and derived indicator arrays are shared by backtest, walk-forward,
  synchronous optimization, and background optimization through `BacktestDataPreparer`.
- Pattern preview and backtest now share `LongEntryFillPolicy` and
  `LongPositionExecutionPolicy`. The shared policy owns long-entry repricing, gap-stop fills,
  same-bar exit priority, partial profit, target/time exits, and next-bar protective-stop updates.
- Live position monitoring delegates stop, target, strategy/time priority, breakeven, and trailing
  decisions to `LiveLongPositionDecisionPolicy`, while broker submission and actual fill lookup stay
  in the live adapter. Both execution policies share the same position state and protective-stop
  calculation.
- Live protective-stop state is persisted on `Position` through the ordered
  `PositionExecutionStateMigration`; process restarts no longer discard initial risk, breakeven, or
  trailing activation. Market-open checks use the injected clock, and daily holding limits count
  observed market bars instead of approximating business days as calendar days.
- Automatic and manual live exits share `LivePositionExitCoordinator`. It atomically claims the
  database position before contacting a broker, persists the broker order ID, waits on ambiguous
  states instead of resubmitting, and finalizes the position plus trade record in one transaction.
  The broker port returns a trackable `BrokerOrder` instead of discarding it as a boolean.
- `Program.cs` is now a 59-line composition root. Health, authentication, order, and backtest APIs
  are registered through feature endpoint modules; startup migration/recovery/seeding and the web
  middleware pipeline have dedicated extensions. A route-table test verifies the extracted public
  routes are registered exactly once.
- Prepared-data execution now runs behind `BacktestSimulationEngine`; `BacktestService` coordinates
  data loading, walk-forward, and optimization instead of owning the date-by-date portfolio loop.
  Execution costs are isolated in `BacktestExecutionCostLedger`, with regression tests covering
  fixed/adaptive slippage and exactly-once commission application. Result/metric construction is
  isolated in `BacktestResultBuilder`.
- `BacktestSignalEntryProcessor` owns the complete ordered new-entry pipeline: shared eligibility,
  regime allocation, correlation blocking, past-only Kelly samples, sizing, and immediate or
  next-open registration. The simulation engine is reduced to daily sequencing and no longer
  performs detector-specific entry orchestration inline.
- `BacktestStrategyRuntimeRegistry` is the single owner of custom-strategy runtime lookup and state
  transitions: reference-data as-of snapshots, daily entry counts, strategy equity peaks, drawdown
  circuit breakers, consecutive-loss cooldowns, and strategy-symbol reentry keys. The simulation
  engine and entry/exit processors no longer exchange mutable runtime/cooldown dictionaries.
- `BacktestTradeLedger` now owns exactly-once cost and realized-equity settlement for every exit
  producer. `BacktestTerminalPositionLiquidator` closes and removes remaining positions before the
  final equity mark; a golden fixture prevents the former double-count of terminal realized profit
  as unrealized profit. `BacktestSimulationEngine` is now a 175-line daily sequencer.
- Realized/unrealized equity, daily loss limits, marked-equity drawdown, and open positions now live
  in `BacktestPortfolioState`. `LongPositionSizingPolicy` is the shared owner for stop-risk capital,
  portfolio caps, minimum Kelly samples, Kelly/Half-Kelly selection, and the 25% Kelly ceiling.
  Daily, one-minute, and weekly golden simulations lock the same entry, target fill, quantity, and
  portfolio return across timeframe variants.
- `BacktestPositionExitProcessor` owns the invariant order of intrabar fills, close-based strategy
  exits, scale-ins, and scale-outs. Strategy cooldown/circuit-breaker transitions are isolated in
  `BacktestStrategyTransitionPolicy`, and NextOpen fills re-run the central sizing cap after repricing.
  The unused legacy `SimulateSymbolAsync` second simulation loop has been removed; the
  remaining adapter only maps the shared long-position execution policy into backtest trade records.
  Live recommendations now use the same capital-cap and whole-share floor helpers. Golden fixtures
  cover NextOpen gap repricing/entry-bar exits and close-based scale-outs in addition to the three
  timeframe baseline.
- `BacktestOpenPositionFactory` is the single backtest position-construction contract for current-close
  and NextOpen entries. `BacktestPendingEntryProcessor` owns delayed-entry eligibility, gap repricing,
  sizing, entry-bar execution, and strategy-state updates. The old `TradeSimulator` name is now
  `BacktestExecutionAdapter`, matching its remaining responsibility instead of implying a second engine.
  End-to-end golden fixtures also lock same-bar stop priority and partial-profit aggregation without
  double-counting the remaining position.
- `PositionAllocationPolicy` owns regime and strategy allocation scaling for backtest, preview, and
  live recommendation paths. `PortfolioCorrelationPolicy` computes aligned historical returns with
  an explicit 60-bar window and 10-return minimum. The simulation engine is now 391 lines and delegates
  correlation blocking instead of calculating Pearson statistics inline. Golden fixtures cover bear
  weight reduction and rejection of a second highly correlated symbol.
- `BacktestEntryEligibilityPolicy` is the single owner of position limits, drawdown and consecutive-loss
  circuit breakers, daily entry limits, and same-symbol reentry cooldown boundaries. Both immediate
  entries and NextOpen pending entries delegate to it, preventing delayed orders from bypassing the
  runtime gates that applied when their signal was created.
- `LongPositionExitPolicyCatalog` now owns built-in pattern defaults, exit overrides, and custom-strategy
  policy construction. Preview, backtest, and live monitoring consume the same `LongPositionExitPolicy`;
  live code no longer reaches into a nested backtest-adapter profile type.
- `LivePositionExitCoordinator` now owns evidence-based reconciliation as well as idempotent submission.
  The background worker and authenticated operator endpoint share it; only a proven terminal failure
  releases a claim and only a proven fill closes the position. Trade, portfolio, and dashboard APIs
  share one open-position response with pending-exit state and elapsed time. The desktop portfolio
  disables duplicate close requests and offers a safe status refresh instead of a force retry.
- `LongPositionCloseDecisionPolicy` is the shared owner of target, strategy-rule, and time-exit
  priority after stop/partial processing. Bar-based preview/backtest and live current-price decisions
  delegate to it. Equivalent-price snapshot fixtures lock stop, target, strategy, time, and hold-state
  parity, including invalid zero-price boundary handling.
- `CumulativeRsi2ExitDecisionPolicy` now owns the built-in strategy's trend-break-first and cumulative
  RSI threshold semantics. Backtest and live monitoring pass their independently prepared indicator
  snapshots into the same pure decision, including the same invalid-price boundary.
- `Tqqq200SmaExecutionPolicy` now owns the TQQQ strategy's entry stop/target and rolling long-trend
  protective-stop calculations. The SMA period and stop/target multipliers are typed settings used
  by detection, prepared backtest data, and live monitoring. Daily data lookback expands with the
  configured period, and parity fixtures lock stop advancement and subsequent triggering.

Remaining Phase 2 work is primarily residual runtime orchestration extraction from
`BacktestSimulationEngine` and broader preview/backtest/live parity fixtures.

## Phase 0 — Guardrails and governance

- Declare the active project and canonical Svelte UI.
- Record architecture and trading invariants.
- Preserve representative daily, intraday, weekly, partial-exit, and next-open behavior in tests.
- Capture baseline result and performance fixtures before engine extraction.

Exit gate: documentation is current and all characterization tests pass.

## Phase 1 — Central policy catalogs

- Extract timeframe facts, backtest range policy, and preview range policy.
- Add indicator, strategy, and data-provider capability catalogs.
- Expose frontend-safe metadata through an API contract.
- Replace duplicated UI catalogs with server metadata.

Exit gate: no feature owns a second copy of shared metadata.

## Phase 2 — Deterministic strategy engine

- Split historical data preparation from simulation.
- Extract timeline, fill, cost, position, portfolio, and metric components.
- Split rule parsing, validation, indicator evaluation, and comparison operators.
- Compile a typed strategy definition once and run it in preview, backtest, and live paths.

Exit gate: the engine runs without EF, ASP.NET, HTTP, broker SDKs, or system time.

## Phase 3 — Application use cases

- Introduce preview, backtest, optimize, scan, evaluate, place-order, and close-position use cases.
- Make endpoints and workers thin adapters.
- Centralize retries, clock access, market sessions, and idempotency.

Exit gate: endpoints and workers contain no strategy or portfolio calculations.

## Phase 4 — Persistence and contracts

- Replace startup SQL with versioned EF Core migrations.
- Store a typed, versioned strategy document with compatibility readers.
- Separate API contracts from EF entities.
- Generate TypeScript contracts from OpenAPI.

Exit gate: no schema-altering SQL exists in `Program.cs`; old databases migrate automatically.

## Phase 5 — Desktop decomposition

- Move state and commands out of large Svelte pages.
- Split strategy builder and backtest screens by user task.
- Remove frontend copies of backend catalogs.
- Freeze and then remove legacy Blazor routes and packages after usage verification.

Progress: `Backtest.svelte` now imports timing/factor research catalogs, symbol-set operations,
formatters, whipsaw classification, and equity-curve volatility from
`features/backtest/backtestResearch.js`. Failure and warning messages, execution metadata, headline
metrics, and the timing report are rendered by `features/backtest/BacktestResultSummary.svelte`.
Performance breakdowns, walk-forward and Monte Carlo validation, and trade history are isolated in
three further display-only components under `features/backtest`. These boundaries reduced the page
from 2,130 to 1,730 lines. Factor-lab insight cards and ranking tables are also rendered by
`BacktestFactorRanking.svelte`, with reusable lift and drawdown comparisons in
`backtestResearch.js`. The factor-lab controls now compose a dedicated custom-experiment editor and
candidate preview table behind `BacktestFactorLabPanel.svelte`. This reduces the page further to
1,499 lines. Timing structure/window controls and the scenario comparison table are now delegated
to `BacktestTimingOptions.svelte` and `BacktestScenarioComparison.svelte`; the parent supplies
comparison deltas and owns scenario selection. The page is now 1,437 lines and remains responsible
for research execution rather than result-table presentation details. Universe filter controls and
their baseline comparison table are likewise isolated in `BacktestUniverseControls.svelte` and
`BacktestUniverseComparison.svelte`. Execution inputs, statistical validation and risk/weight
settings, and pattern selection/run controls now live in three focused components. This reduces
the page to 1,228 lines while preserving the parent's request construction and execution state.

Exit gate: Svelte is the only operational UI and large pages are orchestration shells.

## Phase 6 — Operations cleanup

- Consolidate Docker and compose files around supported deployment paths.
- Make build scripts line-ending independent and non-interactive where safe.
- Add migration, provider, database, and engine version health signals.

Exit gate: one documented local path and one documented K3s production path remain.

## Measurable completion criteria

- `Program.cs` is below 200 lines.
- Central orchestration files are normally below 500 lines.
- Timeframe and indicator metadata each have one owner.
- Domain and Engine have enforced dependency tests.
- Preview/backtest/live parity scenarios use the same compiled strategy.
- No startup schema mutation uses handwritten SQL.
- Every intentional result change has a regression test and explanation.
