# StockTrader architecture

StockTrader is moving from a folder-organized monolith to a modular monolith. It remains one
deployable application while gaining explicit boundaries that reduce the amount of code a person
or AI must understand for one change.

## Target modules

| Module | Owns | Must not own |
| --- | --- | --- |
| Domain | Symbols, money, timeframe identity, strategy and order state | EF, HTTP, UI, configuration |
| Engine | Indicators, rule evaluation, fills, portfolio simulation, metrics | Database and broker access |
| Application | Preview, backtest, optimize, scan, evaluate, order use cases | Provider implementations |
| Infrastructure | SQLite, Alpaca, Yahoo, LS, notifications, system clock | Trading policy |
| API/Workers | Authentication, contracts, scheduling, composition | Strategy calculations |
| Desktop | Presentation and interaction | Duplicated backend catalogs |

Dependencies point inward. Infrastructure implements application ports; the domain never imports
infrastructure.

## Sources of truth

- Timeframe facts: one backend catalog.
- Backtest and preview range policy: dedicated policy catalogs.
- Indicator definitions: one registry that supplies calculation, validation, units, parameters,
  warmup, and supported timeframes.
- Strategy definitions: one typed aggregate compiled for preview, backtest, and live execution.
- API contracts: explicit DTOs, later used to generate TypeScript types.
- Database shape: EF Core migrations; frozen legacy readers only adopt pre-migration databases.
- UI delivery: Svelte assets in the Desktop container; the API never serves application pages.
- Operations: `docker-compose.yml` locally and `scripts/deploy-k3s.sh` for K3s production.

## Current transition seams

The current high-risk files are intentionally decomposed incrementally:

- `Services/Backtest/BacktestService.cs`
- `Services/Patterns/RuleBasedDetector.cs`
- `desktop-app/src/pages/PatternBuilder.svelte`
- `desktop-app/src/pages/Backtest.svelte`

No phase may change all of these at once. Characterization tests are added before extraction.

`Program.cs` has crossed its target boundary: it now owns only host composition and delegates API
registration, startup initialization, and middleware configuration to named feature modules.

Historical market-data preparation now crosses a named boundary:
`Services/Backtest/BacktestDataPreparer.cs` produces the read-only dictionary boundary in
`Application/Backtesting/PreparedBacktestData.cs`. Backtest, walk-forward, and both optimization
execution modes must use this boundary instead of calculating private indicator arrays.
`BacktestSignalEntryProcessor` owns the ordered new-entry pipeline after data preparation:
eligibility, regime allocation, correlation blocking, past-only sizing samples, position sizing,
and current-close versus next-open registration. `BacktestSimulationEngine` only schedules this
pipeline within the daily exit/pending-entry/mark-to-market sequence.
`BacktestStrategyRuntimeRegistry` owns per-strategy equity peaks, drawdown stops, daily entry counts,
consecutive-loss transitions, per-symbol reentry cooldown keys, and reference-data as-of updates.
Entry and exit processors resolve runtime state through this registry instead of sharing mutable
dictionaries or constructing strategy-symbol keys themselves.
`BacktestTradeLedger` is the single settlement boundary for simulated trades: execution costs,
portfolio realized equity, and strategy runtime equity are applied exactly once. Terminal positions
are closed by `BacktestTerminalPositionLiquidator`, which removes each settled position before the
final marked-equity snapshot so realized and unrealized profit cannot be counted twice.

Pattern preview now follows the same adapter/use-case/engine split. `PatternPreviewEndpoints` owns
only HTTP status and response formatting, `PatternPreviewService` compiles the strategy and prepares
provider data, and `PatternPreviewSimulationEngine` replays prepared bars without EF, HTTP, provider
SDKs, or system time. `ICompiledStrategyRuntime` is the application-facing runtime port implemented
by the custom detector. The engine treats `DataTo` as exclusive so a repository-inclusive boundary
bar cannot alter a chart that does not display that bar.
An end-to-end parity fixture compiles one NextOpen custom strategy once, creates fresh runtimes from
that compiled object, and asserts identical preview/backtest entry time, repriced fill, exit time,
exit price, and exit reason.

Long-position execution now crosses a second named boundary in `Application/Execution`.
Pattern preview and backtest must delegate entry repricing and per-bar exit ordering to this pure
policy instead of implementing private OHLC rules. Because OHLC data does not reveal intrabar
ordering, the policy deliberately uses the conservative sequence: the stop known at bar open,
then partial profit, target/strategy/time exit, and finally protective-stop updates that take
effect on the next bar. `LongPositionExitPolicyCatalog` is the sole owner of built-in defaults and
custom-strategy exit-policy construction for preview, backtest, and live monitoring. Live monitoring
uses a separate decision adapter because it submits a real
broker order and records the broker's fill instead of inventing an OHLC fill; it still shares the
same state, `LongPositionCloseDecisionPolicy` target/strategy/time priority, and protective-stop
calculation. Snapshot parity fixtures compare bar-based and live decisions where price ordering is
fully observable. Built-in close rules such as cumulative RSI2 trend-break/threshold decisions also
live in pure execution policies rather than in backtest or worker adapters.
`StrategyEntryEligibilityPolicy` is the corresponding common entry gate. Preview, backtest, and
live recommendation adapters translate their runtime state into the same position-limit,
drawdown, consecutive-loss, session-entry, and reentry decisions. Environment-specific bar/date
bookkeeping stays outside the policy, but it cannot change the gate ordering or effective position
limit. Live recommendation timestamps and cooldown boundaries use the injected `TimeProvider`,
and per-day entry counts reset at the US market calendar's date boundary instead of UTC midnight.
The TQQQ long-trend strategy likewise owns its entry stop/target and rolling SMA stop-floor math in
`Tqqq200SmaExecutionPolicy`. Its configured SMA period and multipliers feed detection, prepared
backtest data, and live monitoring; adapters must not embed their own 200-day or multiplier values.

Live execution state belongs to the persisted `Position`, not a background worker's memory. Its
original risk distance and protective-stop flags survive restarts through an ordered database
migration. Session checks receive a `TimeProvider`, and daily time exits count stored daily bars;
workers must not embed exchange hours or approximate trading sessions from calendar-day ratios.

Live exit submission is a use case, not a broker call hidden in a worker or UI. All automatic and
manual exits first atomically claim the persisted position, then store the broker's order ID. A
restart reconciles that order as filled, terminally failed, or still uncertain; uncertain orders
are never blindly resubmitted. Position closure and its trade record commit atomically.
`LivePositionExitCoordinator` owns both submission and evidence-based reconciliation. Background
monitoring and the operator API call the same use case. Position responses use one contract that
shows whether an exit is ready, missing a confirmed broker order ID, or awaiting broker resolution;
the operator can request reconciliation but cannot force-clear an uncertain order.

Database schema ownership now crosses a fail-closed EF Core boundary. Empty databases are created
from generated migrations and databases with EF history apply only their pending migrations. The
temporary legacy baseline writer has completed production adoption and is removed. A database with
application tables but no EF history is rejected before any schema or row change.

## Decision records

- `adr/0001-modular-monolith.md`: why a modular monolith is the target.
- `adr/0002-custom-rule-evaluation-pipeline.md`: deterministic ownership of custom-rule indicators,
  conditions, groups, dynamic price levels, reference history, and observation time.
- `adr/0003-ef-core-migration-baseline.md`: safe adoption of EF schema history by new and legacy SQLite
  databases.
- `adr/0004-retire-handwritten-migrations.md`: fail-closed retirement of the temporary legacy
  schema writer after production baseline adoption.
- `refactoring-roadmap.md`: migration order, gates, and measurable completion criteria.
