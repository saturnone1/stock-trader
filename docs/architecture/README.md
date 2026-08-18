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
`BacktestPreparedSimulationRunner` owns prepared-range slicing and invocation of the deterministic
simulation engine, `BacktestRegimeMapBuilder` owns benchmark regime construction, and
`BacktestOptimizationService` owns candidate search plus IS/OOS ranking. `BacktestService` remains
the sub-500-line application coordinator for an ordinary run and walk-forward validation.
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
exit price, and exit reason. It also runs the live fill and extracted live exit evaluator against
the same compiled strategy, locking entry risk geometry and the observable exit reason.

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
`LongPositionScalingPolicy` likewise owns original-entry-based share rounding, scale-in weighted
average price, adapter-supplied capital-cap enforcement, scale-out remaining cost, and post-fill
execution counting. Preview and backtest only translate its decision into markers or trade records.
Live trading fails closed for scaling strategies until the
broker adapter can persist and reconcile equivalent partial-order state.
`LivePositionExitEvaluator` owns live bar loading, ATR preparation, built-in indicator snapshots,
custom sell-rule evaluation, and translation into the shared decision policy. The 230-line
`PositionExitManagerService` now owns only scheduling, broker state, persistence, and durable exit
coordination. Entry ATR period and live-exit lookback values come from `StrategyEvaluationPolicy`.
`StrategyEntryEligibilityPolicy` is the corresponding common entry gate. Preview, backtest, and
live recommendation adapters translate their runtime state into the same position-limit,
drawdown, consecutive-loss, session-entry, and reentry decisions. Environment-specific bar/date
bookkeeping stays outside the policy, but it cannot change the gate ordering or effective position
limit. Live recommendation timestamps and cooldown boundaries use the injected `TimeProvider`,
and per-day entry counts reset at the US market calendar's date boundary instead of UTC midnight.
Next-open risk geometry is likewise centralized: preview and backtest call
`LongEntryFillPolicy.Reprice`, while live order persistence calls
`LongEntryFillPolicy.ReanchorExecutedFill` after reading the broker's actual average fill. A direct
golden compiles one strategy once and compares preview, backtest, and live entry price, stop, and
target. Live daily scanning uses the injected application clock and the same central regime period
and lookback values instead of private `DateTime.UtcNow`, 200-bar, or 400-day constants.
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

Stored custom strategies now carry a `DocumentVersion` that is independent from the compiled-engine
schema version. The compatibility policy reads legacy unversioned requests, stamps every successful
write with the current document version, and rejects unknown future versions rather than guessing
their semantics. Existing rows are adopted through an ordered EF migration.
The application exposes a migration-only process mode so deployment can back up SQLite, apply the
ordered migration, and exit before the API starts. The canonical K3s script stops the sole writer,
creates and integrity-checks a SQLite backup, runs that mode, and only then rolls out the API image.
The custom-strategy HTTP boundary no longer binds or returns the EF entity. A write contract contains
only client-editable fields, a response contract contains explicit public fields, and one mapper owns
the translation used by create, update, list, detail, backtest-apply, and preview requests. Document
defaults are shared by persistence and API contracts through `StrategyDocumentDefaults`.
The current document version is emitted by strategy-builder metadata, so even the desktop's local
preset uses the Domain-owned value instead of copying a version literal.
Build-time OpenAPI generation now produces the committed desktop schema and TypeScript components
without reading secrets, migrating the database, or starting hosted workers. Strategy CRUD desktop
types consume those generated components. Backtest, optimization, preview, and runtime compilation
use `StrategyDocument`, which carries strategy semantics and an optional stored-strategy reference
without EF-only keys or audit timestamps. The desktop performs the stored-response conversion
explicitly before research requests, and OpenAPI no longer exposes the EF entity.
`CustomPatternManagementService` is the application boundary for strategy CRUD and optimization
promotion. It validates every `StrategyDocument` with `StrategyCompiler`, owns identity/version/
timestamps and case-insensitive name conflicts, and persists `StoredStrategy` through
`ICustomPatternStore`. The port contains no EF types; `CustomPatternDefinition` conversion and unique
constraint translation live solely in the SQLite adapter. HTTP endpoints and the automatic optimizer
cannot write strategy rows directly. Invalid optimization output is rejected without changing the
stored strategy or incrementing its applied-result count.
Stored display names have a separate server-owned normalized key with a database unique index.
The application pre-check provides an early conflict response, while the persistence adapter maps
the remaining concurrent-write race to the same typed conflict result.

## Decision records

- `adr/0001-modular-monolith.md`: why a modular monolith is the target.
- `adr/0002-custom-rule-evaluation-pipeline.md`: deterministic ownership of custom-rule indicators,
  conditions, groups, dynamic price levels, reference history, and observation time.
- `adr/0003-ef-core-migration-baseline.md`: safe adoption of EF schema history by new and legacy SQLite
  databases.
- `adr/0004-retire-handwritten-migrations.md`: fail-closed retirement of the temporary legacy
  schema writer after production baseline adoption.
- `adr/0005-version-strategy-documents.md`: version persisted strategy definitions independently
  from compiled-engine semantics and fail closed on unknown future documents.
- `adr/0006-strategy-management-use-case.md`: route every persisted strategy mutation through one
  validated application use case and a purpose-specific persistence port.
- `adr/0007-generate-desktop-api-contracts.md`: generate committed desktop TypeScript contracts from
  side-effect-free build-time OpenAPI metadata and reject drift in CI.
- `adr/0008-separate-strategy-document-from-storage.md`: keep preview, backtest, optimization, and
  runtime compilation independent from the EF storage entity.
- `refactoring-roadmap.md`: migration order, gates, and measurable completion criteria.
