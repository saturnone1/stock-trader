# Refactoring roadmap

## Current progress

- Central timeframe, indicator, operator, strategy, and provider catalogs are active.
- Trading-account state now crosses an application persistence port, active-account changes are
  transactional, broker clients are constructed behind a focused factory, and explicit API contracts
  keep secrets write-only. The desktop derives broker environments from the central broker catalog.
- Automatic and manual live entries now resolve one account/broker snapshot and share
  `LiveEntryExecutionCoordinator`. Broker adapters return order evidence, and the accepted
  recommendation plus position are committed through one purpose-specific transactional store.
- Preview, backtest, and live rule evaluation consume the same compiled strategy model.
- Live strategy loading is behind `ICompiledStrategyRepository`.
- Database startup changes run through ordered, versioned migrations.
- Optimization contracts, search-space generation, strategy variants, and ranking are isolated in
  `Application/Optimization`.
- Historical data preparation and derived indicator arrays are shared by backtest, walk-forward,
  synchronous optimization, and background optimization through `BacktestDataPreparer`.
- Pattern preview and backtest now share `LongEntryFillPolicy` and
  `LongPositionExecutionSessionPolicy`. The session composes the lower-level bar and scaling
  policies and atomically owns long-entry state projection, gap-stop fills, same-bar exit priority,
  partial profit, target/strategy/time exits, scale-in/out, realized PnL, weighted cost, execution
  counts, and next-bar protective-stop updates.
- Live position monitoring projects current-price snapshots through
  `LiveLongPositionExecutionAdapter` into the same `LongPositionExecutionSessionPolicy` used by
  preview and backtest. The adapter emits one durable broker intent at a time; quantity and
  partial-profit state change only after a broker-confirmed fill. Built-in and custom partial-profit
  strategies therefore share the same sizing and priority semantics in research and live trading.
  Custom scaling now uses the same compiled detector, original-entry sizing, persisted execution
  counts, and central capital cap in live evaluation. Missing broker equity fails scale-in closed
  without blocking risk-reducing scale-outs.
- Live protective-stop state is persisted on `Position` in the EF-owned schema; process restarts
  no longer discard initial risk, breakeven, or
  trailing activation. Market-open checks use the injected clock, and daily holding limits count
  observed market bars instead of approximating business days as calendar days.
- Automatic and manual live position changes share `LivePositionExecutionCoordinator`. It atomically
  claims the database position before contacting a broker, persists the broker order ID, waits on
  ambiguous states instead of resubmitting, and atomically applies a quantity-matched full exit,
  partial profit, scale-in, or scale-out fill. Requested kind, quantity, strategy meaning, and scaling
  rule survive restarts. Broker acknowledgements and fills with mismatched symbol, direction, or
  quantity fail closed. The broker port returns a trackable `BrokerOrder` and exposes explicit
  quantity buy and sell contracts instead of discarding submission evidence as a boolean.
- `Program.cs` is now a 59-line composition root. Health, authentication, order, and backtest APIs
  are registered through feature endpoint modules; startup migration/recovery/seeding and the web
  middleware pipeline have dedicated extensions. A route-table test verifies the extracted public
  routes are registered exactly once.
- Prepared-data execution now runs behind `BacktestSimulationEngine`; `BacktestService` coordinates
  ordinary runs and walk-forward instead of owning the date-by-date portfolio loop. Prepared-range
  slicing, benchmark regime construction, and parameter-search orchestration now live in
  `BacktestPreparedSimulationRunner`, `BacktestRegimeMapBuilder`, and `BacktestOptimizationService`.
  The coordinator has dropped from 732 to 382 lines and has an architecture cap of 500 lines.
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
- `BacktestPositionExitProcessor` delegates the invariant order of intrabar fills, close-based
  strategy exits, scale-ins, and scale-outs to the common position session.
  `BacktestStrategyExecutionInstructionResolver` is the sole backtest adapter for custom exit and
  scaling rules, including the central scale-in capital cap, and is shared by ordinary held bars and
  NextOpen entry bars. Strategy cooldown/circuit-breaker transitions are isolated in
  `BacktestStrategyTransitionPolicy`, and NextOpen fills re-run the central sizing cap after repricing.
  The unused legacy `SimulateSymbolAsync` second simulation loop has been removed; the
  remaining adapter only maps the shared long-position execution policy into backtest trade records.
  Live recommendations now use the same capital-cap and whole-share floor helpers. Golden fixtures
  cover NextOpen gap repricing, custom-rule exits and scale-outs on the entry bar, and close-based
  scale-outs in addition to the three timeframe baseline.
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
- `LivePositionExecutionCoordinator` now owns evidence-based reconciliation as well as idempotent submission.
  The background worker and authenticated operator endpoint share it; only a proven terminal failure
  releases a claim and only a proven quantity-matched fill reduces or closes the position. Trade,
  portfolio, and dashboard APIs share one open-position response with pending-exit state, requested
  quantity, and elapsed time. The desktop portfolio
  disables duplicate close requests and offers a safe status refresh instead of a force retry.
- Live position indicator preparation and strategy evaluation now run behind
  `LivePositionExecutionEvaluator`; `PositionExecutionManagerService` is capped at 250 lines and
  coordinates only polling, broker prices, persistence, and durable position-order submission. The evaluator
  is 265 lines and consumes the central ATR period, indicator lookback, exit catalog, TQQQ stop,
  cumulative RSI2 decision, and custom compiled runtime policies.
- `LongPositionCloseDecisionPolicy` is the shared owner of target, strategy-rule, and time-exit
  priority after stop/partial processing. Bar-based preview/backtest and live current-price decisions
  delegate to it. Equivalent-price snapshot fixtures lock stop, target, strategy, time, and hold-state
  parity, including invalid zero-price boundary handling.
- `StrategyEntryEligibilityPolicy` now owns the final custom-strategy entry gate for preview,
  backtest, and live recommendations. All three paths share the same effective position-limit and
  block priority for drawdown, consecutive losses, per-session entries, and reentry cooldowns.
  Backtest bar indexes and live calendar dates remain adapter state; live evaluation now receives
  its observation time from `TimeProvider` rather than the system clock, and daily entry limits use
  the US market date rather than resetting early at UTC midnight.
- Pattern preview is split into a 125-line HTTP adapter, a data-preparation use case, and a pure
  prepared-bar simulation engine. The engine receives `ICompiledStrategyRuntime`, precomputed causal
  ATR values, reference-data as-of boundaries, and the same long-position entry/exit policies used
  by backtest. Goldens lock NextOpen gap repricing, entry-bar exits, prepared-indicator prefix
  stability, and exclusion of a hidden repository-inclusive end bar. A cross-engine fixture also
  compiles one strategy once and locks identical preview/backtest entry events plus the live
  broker-fill entry, stop, and target geometry. Live order persistence now reuses
  `LongEntryFillPolicy`, and both live scanning and order timestamps use `TimeProvider`.
- `CumulativeRsi2ExitDecisionPolicy` now owns the built-in strategy's trend-break-first and cumulative
  RSI threshold semantics. Backtest and live monitoring pass their independently prepared indicator
  snapshots into the same pure decision, including the same invalid-price boundary.
- `Tqqq200SmaExecutionPolicy` now owns the TQQQ strategy's entry stop/target and rolling long-trend
  protective-stop calculations. The SMA period and stop/target multipliers are typed settings used
  by detection, prepared backtest data, and live monitoring. Daily data lookback expands with the
  configured period, and parity fixtures lock stop advancement and subsequent triggering.
- `RuleIndicatorEvaluator` now owns custom-rule indicator math and per-evaluation caches. The
  1,106-line `RuleBasedDetector` is reduced to a 482-line strategy orchestration shell and no longer
  embeds RSI, MACD, volatility, price-structure, volume, ADX, or stochastic calculations. Direct
  goldens lock catalog defaults, current/previous bar offsets, context-local caching, and neutral
  handling of unknown indicators while the existing detector suite preserves end-to-end behavior.
- `RuleConditionEvaluator` now owns single-rule history guards, fixed/indicator thresholds,
  consecutive/within-bar semantics, reference-symbol as-of filtering, and all six comparison
  operators. `RuleGroupEvaluator` is the single owner of nested AND/OR composition, matched/total
  weight accounting, and user-facing matched-condition explanations. These pure boundaries reduce
  `RuleBasedDetector` further to entry/exit/scaling orchestration and dynamic price-level selection.
- `DynamicExitPricePolicy` now owns custom-strategy ATR, percent, prior-range, moving-average,
  Bollinger, and R-multiple initial stop/target selection. It reuses the rule evaluation context and
  excludes the current bar from prior-range levels. `RuleBasedDetector` only rejects levels that are
  invalid for a long entry and assembles the resulting signal.
- Custom-rule signal timestamps now come from an explicitly supplied `TimeProvider`; the detector
  no longer reads `DateTime.UtcNow`. Live scanning and preview receive the application clock, while
  tests can replay an identical observation timestamp without changing strategy semantics.
- `ICustomStrategyDetector` is now the runtime contract used by preview, backtest, optimization,
  scanning, and live exits. `CustomStrategyDetectorFactory` is the sole production constructor for
  `RuleBasedDetector`; production code no longer creates or casts the concrete detector directly,
  and every execution receives a fresh runtime over the same compiled-strategy semantics.
- EF Core now owns the generated initial schema, model snapshot, and future schema history. Empty
  databases migrate directly through EF. The one-time production compatibility bridge completed
  baseline adoption and has been removed; an unbaselined database now fails closed without writes.
  Tests cover empty creation, row preservation, idempotency, and legacy refusal. `/api/health`
  reports the applied/latest EF migration, pending count, and synchronization state.
- Stored custom strategies now have an explicit `DocumentVersion`. Legacy unversioned API payloads
  remain readable, successful writes are stamped with the current version, unknown future versions
  fail compilation, and an ordered EF migration adopts existing rows without rewriting strategy JSON.
- Custom-strategy create, update, read, and preview endpoints now use explicit write/response
  contracts instead of accepting or returning the EF entity. Server-owned identity and audit fields
  cannot be written by clients, and a single mapper plus central document defaults preserves the
  existing JSON wire shape without coupling it to future database columns.
- Strategy CRUD and optimization promotion now delegate to `CustomPatternManagementService` through
  `ICustomPatternStore`. The application boundary owns compilation validation, server fields,
  duplicate names, and clock access. Invalid optimized parameters can no longer bypass the compiler
  and corrupt a stored strategy, and the 63-line HTTP module contains no EF or business validation.
- Stored strategy names now use a server-owned normalized comparison key and database unique index.
  Create/update races that pass the application pre-check are translated by the SQLite adapter into
  the same typed name-conflict outcome, while the normalized key remains absent from API contracts.
- ASP.NET Core now emits a committed OpenAPI document during build without starting migrations,
  secrets, or hosted workers. `openapi-typescript` generates the desktop schema file, strategy read
  and write types consume those generated components, and CI rejects OpenAPI or TypeScript drift.
- Backtest, optimization, preview, and runtime compilation now accept a storage-independent
  `StrategyDocument`. It has an optional stored-strategy reference but no normalized key or audit
  timestamps. The desktop strips persistence metadata explicitly, and generated OpenAPI no longer
  exposes `CustomPatternDefinition`.
- Strategy CRUD and promotion now use `StoredStrategy` through `ICustomPatternStore`. EF strategy
  rows are translated only inside the SQLite adapter; application and API source cannot reference
  the persistence entity, and mapper round-trip tests protect every strategy field.
- `LongPositionScalingPolicy` now owns original-entry-based scale quantity rounding, scale-in
  weighted-average price, scale-out remaining cost, and adapter-supplied capital caps. Preview and
  backtest no longer disagree between nearest-share rounding and truncation, and backtest preserves
  the original quantity after partial exits. Rule execution counts advance only after an actual
  scaling fill and belong to each open position rather than an orchestration service dictionary,
  preserving limits across processor recreation and position-state copies. Live persistence and
  broker reconciliation now honor this contract; strategy compatibility remains explicitly rejected
  until the live evaluator supplies identical scaling instructions and the central scale-in capital cap.
- `OptimizationJobExecutionPolicy` now owns OOS splitting, deterministic evenly distributed 60/40 search
  planning, duration boundaries, and restart chunk calculations. `OptimizationBacktestAssumptions`
  is the single cost baseline for synchronous and background candidate evaluation. Both workers use
  injected `TimeProvider` for timestamps and polling. `IOptimizationCandidateEvaluator` now owns the
  shared strategy-variant, timeframe-selection, prepared-simulation, and failure-handling path for
  synchronous and background IS/fine/OOS runs; `OptimizationResultProjection` owns their metric units.
  `IOptimizationEvaluationContextPreparer` supplies both modes with one resolved feed, central regime
  benchmark, reference-symbol set, timeframe data map, and risk input. `OptimizationJobExecutor` has
  dropped from 579 to 330 lines after `IOptimizationJobExecutionStore` also absorbed persisted control
  signals, candidate JSON, chunk checkpoints, result-row mapping, and OOS-only updates. It is guarded
  by a 350-line cap and no longer references the broad repository contract.
- `IOptimizationJobLifecycle` now owns Pending selection and every execution status transition. The
  scheduler and executor exchange a storage-independent ticket rather than `OptimizationJob`; both
  background components are free of `Data` and `Models` imports. The 174-line polling worker is
  guarded by a 200-line cap. Pending jobs are claimed by a status-guarded database update, preventing
  two concurrent workers from starting the same row. Ranked chunk results and their restart
  checkpoint now commit in one SQLite transaction, with failure-injection coverage proving that a
  result-write failure also rolls back the progress advance. `OptimizationJobControlService` now
  owns pause, resume, and cancel legality through a pure transition policy. Its SQLite port applies
  status-guarded updates, so concurrent operator commands cannot overwrite one another, and startup
  recovery no longer resolves the broad optimization repository. Optimization job API timestamps
  and elapsed-time projections now use the injected application clock. Creation, list/detail
  queries, settings, result reads, and conditional terminal deletion now use
  `OptimizationJobManagementService`; the HTTP module is down from 462 to 212 lines and no longer
  imports persistence entities, repositories, or JSON. Combination counting and progress/remaining
  projections are application policies. The SQLite mapper now preserves each stored result ID,
  fixing manual result application that previously sent `null` and selected the automatic candidate.
- Optimization result promotion is now a scoped application use case behind
  `IOptimizationAutoTuneStore`. The ranking policy no longer consumes EF results, apply counters use
  an atomic SQL increment, and continuous recycling deletes old results and resets the job in one
  transaction. Persisted request and candidate JSON are confined to the SQLite adapter.
- The broad `IOptimizationRepository` and its pass-through implementation have been removed.
  Execution storage and lifecycle now own their EF operations directly, with targeted column updates
  and SQLite tests for concurrent claims, chunk rollback, progress isolation, and OOS isolation.
- Built-in detector registration and backtest override construction now iterate one
  `BuiltInPatternDetectorCatalog`. Enum-coverage and construction tests prevent missing strategies;
  this exposed and fixed the omitted TQQQ 200-SMA detector in live and research execution.
- Stock recommendation probability, expected return, downside risk, stop, target, confidence, and
  grade calculations now live in the deterministic `StockRecommendationPolicy`. Indicator snapshot
  composition has a focused factory, operational cache/lookback/concurrency values use validated
  options, and the coordinating analysis service uses the injected clock and is below 450 lines.
  Characterization fixtures preserve the prior neutral and weighted-pattern outputs.
- `TimeFrame` and `DataSource` now live beside their central catalogs in `Domain.MarketData` instead
  of the broad legacy model namespace. Their declaration order and JSON names are unchanged, and an
  architecture test prevents Domain from regaining a dependency on `StockTrader.Models`.
- `DataProviderCatalog` now also owns each provider's market-regime benchmark. Backtest,
  optimization, preview, scanning, analysis, daily synchronization, and ML training resolve the
  same benchmark from the effective provider instead of embedding `SPY` or `069500`. The daily sync
  policy continuously includes that benchmark even when it is absent from the watchlist, and both
  daily sync and ML training use the injected application clock.
- Rule-indicator execution now uses `RuleIndicatorCalculatorRegistry`, which fails fast unless its
  implementations exactly cover the central `IndicatorCatalog`. Per-symbol caching, category
  calculators, and hand-written math have separate bounded components; the former 649-line
  `RuleIndicatorEvaluator` is now a 43-line offset-and-dispatch boundary used by every compiled
  custom-strategy execution path.
- API containers now have one listener configuration: `ASPNETCORE_HTTP_PORTS=5239`. Kestrel JSON
  and `ASPNETCORE_URLS` overrides were removed; K3s and Compose expose their public ports by mapping
  to the same container port, eliminating the former 8080/3000/5239 override chain.
- The legacy Blazor application, MudBlazor/ApexCharts packages, static assets, and server-rendered
  routes have been removed. The backend now returns JSON problem details on failures and carries no
  UI framework dependency; Svelte is the only operational UI.
- Deployment now has one local Compose definition and one K3s deployment script. The broken legacy
  full-stack Dockerfile, four competing Compose variants, single-process K3s manifests, and duplicate
  build/deploy scripts were removed. Desktop uses same-origin `/api` routing in Vite, nginx, and K3s,
  and API rollouts use `Recreate` so two application Pods never share the production SQLite file.
- Production baseline evidence allowed the handwritten SQLite bridge to be retired. Startup now
  accepts only an empty database or one with EF migration history and fails closed without writes
  for an unbaselined legacy database. EF Core is the sole schema mutation engine.
- Backtest slippage identity, labels, descriptions, and the default now live in the domain-owned
  `BacktestExecutionCatalog`. The versioned strategy-builder metadata projects that catalog to the
  desktop, where a fail-closed adapter supplies both the execution selector and its explanation.
  Optimization entry and sizing candidates likewise render the existing server strategy catalog
  instead of retaining separate arrays in the page and form component.
- Optimization ranking codes, labels, default selection, legacy alias normalization, and metric
  identity now live in `OptimizationRankingCatalog`. Synchronous result ranking and OOS-aware
  automatic promotion both use one `OptimizationRankingPolicy`, while the desktop obtains the full
  list (including annualized return) from versioned server metadata. Unknown stored or submitted
  values normalize deterministically to Sortino instead of leaking arbitrary strings into jobs.
- Walk-forward period construction and aggregate metrics now live in deterministic
  `WalkForwardAnalysisPolicy`, while `WalkForwardAnalysisRunner` owns one-time data preparation and
  repeated execution through `BacktestPreparedSimulationRunner`. Zero/negative month inputs now
  fail closed with a warning instead of creating a non-progressing loop. IS/OOS and consecutive
  windows use disjoint calendar ranges, removing the shared boundary-day look-ahead leak. Every
  window also receives the request's portfolio weight strategy, and the previously hardcoded zero
  OOS Sharpe field now reports the explicitly labelled average of window OOS Sharpe values. A date
  range too short for one complete IS/OOS window is surfaced as a warning rather than a zero-valued
  analysis.
- Completed-trade state transitions now live in the application-owned
  `StrategyTradeTransitionPolicy` and `StrategyDrawdownPolicy`. Preview and backtest use the same
  bar/step transition for loss versus win cooldowns, trailing-loss reset, circuit-breaker duration,
  peak equity, and permanent maximum-drawdown blocking. Live recommendation eligibility projects
  persisted trades through `StrategyHistoricalCooldownPolicy` and the same drawdown policy, with a
  weekday-aware adapter for its calendar-based polling model. Goldens lock independent symbol and
  global timelines, weekend boundaries, exact drawdown thresholds, and a full preview sequence of
  loss, re-entry delay, consecutive-loss halt, and resumed entry. The consolidation also corrected
  a live-only off-by-one defect that previously released a two-session cooldown after only one full
  trading day.

Remaining Phase 2 work is no longer contract or EF-entity separation. Current full-strategy goldens
cover NextOpen preview/backtest/live fill and exit decisions, NextOpen entry-bar custom exits and
scale-outs, fractional scale-out preview/backtest parity, and multi-symbol indicator cache isolation
against per-symbol previews. Live scaling and new entries now flow through durable execution and
reconciliation contracts. The operator API and portfolio UI expose one generic position-order status
contract, including the order kind, instead of describing scale-ins as pending exits. Broker account,
position, history, entry, scaling, exit, and cancellation capabilities now have one domain catalog;
the API and desktop project it, and live coordinators reject unsupported operations before claiming
durable state. The unused keyed-DI/default-broker construction path has been removed. Remaining Phase
2 work is further narrowing orchestration boundaries around the shared strategy and execution policy.
Live position execution now depends on `ILivePositionExecutionStore`, a four-operation application
port for claim, broker evidence, release, and atomic fill commit. Its SQLite adapter owns isolated
contexts and transaction handling. The remaining broad `ITradeRepository` has also been retired:
trade history, open positions, recommendations, and signals use separate application ports and
isolated contexts. Open-position entities are no longer shared through a mutable read cache.
Operator-triggered full exits and entry/position reconciliation now delegate to
`ILiveOrderManagement`; the HTTP adapter no longer selects accounts or calls execution stores and
coordinators directly. Persisted positions route through their owning account, including
risk-reducing exits for disabled accounts, while legacy account-less positions use an explicit
active-account fallback.

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

Progress: the risk overview endpoint now delegates its complete projection to
`IRiskOverviewQuery`; deterministic policies own R-multiple, holding-day, and halt-alert timing.
Risk services and monitoring use one injected observation clock per evaluation, monitor thresholds
are validated typed options, and legacy accountless positions contribute to portfolio PnL exactly
once. The endpoint contains no persistence access or portfolio arithmetic.

Portfolio performance now delegates to `IPortfolioPerformanceQuery` and a deterministic policy.
Maximum drawdown starts from validated account equity, completed trades have a stable exit-time and
ID order, and the query explicitly reads complete history instead of inheriting the 1,000-row API
page default. `IOpenPositionQuery` also supplies portfolio, trade, and dashboard position lists from
one observation time and durable-order status policy. The HTTP adapters map explicit contracts and
own no position-status or performance formulas.

Exit gate: endpoints and workers contain no strategy or portfolio calculations.

## Phase 4 — Persistence and contracts

- Replace startup SQL with versioned EF Core migrations.
- Store a typed, versioned strategy document with compatibility readers.
- Separate API contracts from EF entities.
- Generate TypeScript contracts from OpenAPI.

Progress: settings reads and writes now cross explicit generated contracts and an application
management use case. The API no longer binds the EF settings entity, secret values are write-only,
and catalog/risk/watchlist validation completes before the SQLite adapter mutates state. The
injected clock is the sole owner of user-visible modification time.

Signal persistence now distinguishes deterministic event time (`SignalBarAt`) from live observation
time. New signals are idempotent per named strategy and evaluated bar, while the EF migration keeps
legacy rows intact. Symbol-profile assignment also crosses an application service and purpose-built
store: API routes and live detection no longer query EF directly, activation updates are atomic,
explicit OpenAPI contracts preserve the existing wire format, and central catalog/symbol policies
validate writes before persistence.

Live signal recommendation now reads completed strategy trades, total open positions, executed
session entries, and ticker sectors through `ILiveSignalEvaluationStore`. Its snapshot contains no
EF entities, and cooldown, drawdown, and sizing rules consume the persistence-independent
`StrategyCompletedTrade` projection. The application clock remains the sole owner of the US
market-day boundary passed into the adapter.

Research universe and financial-factor routes now delegate market-cap percentile ranking, facets,
growth and turnaround math, latest-snapshot selection, comparison summaries, and import-run reads
to application services over `IResearchUniverseStore`. Explicit HTTP contracts replace anonymous
responses, while manual, file, and SEC imports share the application import model, central symbol
normalization, and injected time instead of depending on API DTOs or endpoint-owned EF contexts.

Financial file and SEC collection now cross `IFinancialCollectionStore`; only its SQLite adapter
owns import-run entities and ticker queries. Both coordinators use the injected clock. SEC symbol
selection, automatic-run timing, compatible run identity, XBRL annual-fact selection, amended
filing precedence, price-based market-cap enrichment, and financial ratios are isolated policies
with regression tests. This reduces the SEC coordinator from 584 to 316 lines while preserving the
distinct interval-skip and displayed-success meanings.

Exit gate: no schema-altering SQL exists in `Program.cs`; old databases migrate automatically.

## Phase 5 — Desktop decomposition

- Move state and commands out of large Svelte pages.
- Split strategy builder and backtest screens by user task.
- Remove frontend copies of backend catalogs.
- Keep the Svelte application as the only operational UI.

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
Timing overlays, factor experiment normalization/scoring, universe variants, and scenario-plan
composition are pure functions in `backtestScenarioPlanning.js`, covered by Node golden tests for
source immutability, conservative OR exits, deduplication, and ranking formulas. This reduces the
page to 955 lines. The new tests also fixed order-dependent universe deduplication that previously
could execute the same symbol set twice. Baseline selection, scenario deltas, timing reports,
universe summaries, and factor ranking view models now live in `backtestResultAnalysis.js`, reducing
the page to 787 lines. Result-analysis and research goldens also fixed two trust defects: API
`returnPct` losses were excluded from whipsaw counts, and daily/weekly holding bars were derived
from wall-clock minutes instead of their calendar cadence. Request payload construction, optional
portfolio-weight serialization, plain execution, and sequential multi-scenario orchestration now
live in `backtestExecution.js`. API-contract and orchestration goldens cover numeric normalization,
data-source defaults, timing overlays, progress ordering, and result metadata. `Backtest.svelte` is
no longer an API request builder. Canonical reset state and provider-lookback warnings now live in
`backtestWorkspace.js`; `backtestFactorLab.js` owns financial-factor response projection; and
`backtestViewModel.js` derives selected patterns, scenario counts, cache-safe factor variants,
comparison rows, and timing reports from one state snapshot. Goldens verify independent reset
state, exact factor-query payloads, execution eligibility, and rejection of stale factor caches.
`Backtest.svelte` is now a 457-line state coordinator, down from 2,130 lines, and its architecture
guard enforces the normal 500-line orchestration limit.

`PatternBuilder.svelte` decomposition has started at the strategy-safety boundary. Rule, sell-group,
weight-tier, scaling, portfolio, and live-execution compatibility validation now resides in the pure
`patternValidation.js` module. Node goldens prevent empty sell/scaling conditions, conflicting bar
lookbacks, invalid MACD periods, and unsupported live features from silently reaching persistence.
The page was initially reduced from 1,630 to 1,553 lines. Workspace hydration and API payload
serialization now live in `patternWorkspace.js`, configured from the server-provided indicator and
dynamic-exit catalogs. Goldens cover legacy flat-rule promotion, malformed JSON recovery, parameter
aliases, numeric sanitization, and grouped-strategy round trips. This reduces the page further to
1,253 lines. Add, remove, move, duplicate, and nested-condition commands are now immutable state
transitions in `patternEditorCommands.js`, reducing the page to 1,166 lines. Command goldens cover
all list boundaries, parent reselection, deep-copy behavior, and invalid-index no-ops. They also fix
a trust defect where adding the first condition after deleting every group silently inserted both a
default condition and the requested condition. Workspace selection/creation, the strategy tree,
and the rule/settings inspector are now
rendered by `PatternWorkspaceSidebar.svelte`, `PatternStrategyTree.svelte`, and
`PatternRuleInspector.svelte`. Server strategy-builder metadata is now projected by
`patternMetadata.js`, selected-node chart explanations and preview payloads live in
`patternPreviewModel.js`, and stable Korean UI terminology lives in `patternBuilderUiCatalog.js`.
Strategy CRUD payloads, response hydration, and server-error projection live behind
`patternPersistence.js`. Goldens lock catalog ordering/defaults, fail-closed behavior,
selected-rule lookup, the exact chart explanation contract, and create/open/save/delete API
contracts. `PatternBuilder.svelte` is consequently a 491-line state coordinator, down from 1,630
lines; an architecture test prevents metadata projection, persistence contracts, preview selection
logic, or UI terminology from drifting back into the page.

`Optimization.svelte` now delegates its complete input surface and job/result presentation to
`OptimizationJobForm.svelte` and `OptimizationJobList.svelte`. Request validation, API payload
construction, combination estimates, labels, and result insight calculations live in the pure
`optimizationModel.js` module with Node goldens. The page is a 328-line API/state coordinator,
down from 1,017 lines. The extraction also fixed a result-detail runtime failure caused by the
previous page calling an undefined signed-percent formatter whenever median comparisons existed.

The legacy Blazor route tree and its MudBlazor/ApexCharts packages are removed. `Program.cs` maps
only the JSON API, and the backend project no longer compiles or publishes a second UI.

Exit gate: Svelte is the only operational UI and large pages are orchestration shells.

## Phase 6 — Operations cleanup

- Consolidate Docker and compose files around supported deployment paths.
- Make build scripts line-ending independent and non-interactive where safe.
- Add migration, provider, database, and engine version health signals.

Progress: local container operation is consolidated in `docker-compose.yml`; production K3s builds,
imports, applies the split manifests, and verifies rollouts through `scripts/deploy-k3s.sh`. API and
Desktop each have one Dockerfile. Obsolete single-process manifests and deployment scripts are gone.
Before a schema-changing API rollout, the canonical K3s path now stops the sole SQLite writer,
creates an integrity-checked backup, executes the API's migration-only mode, and starts the new image
only after the schema reports synchronized.

Exit gate: one documented local path and one documented K3s production path remain.

## Measurable completion criteria

- `Program.cs` is below 200 lines.
- Central orchestration files are normally below 500 lines.
- Timeframe and indicator metadata each have one owner.
- Domain and Engine have enforced dependency tests.
- Preview/backtest/live parity scenarios use the same compiled strategy.
- No startup schema mutation uses handwritten SQL.
- Every intentional result change has a regression test and explanation.
