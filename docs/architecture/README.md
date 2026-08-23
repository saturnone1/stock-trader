# StockTrader architecture

StockTrader is moving from a folder-organized monolith to a modular monolith. It remains one
deployable application while gaining explicit boundaries that reduce the amount of code a person
or AI must understand for one change.

The next architecture direction is selective, evidence-led service extraction rather than a
big-bang microservice rewrite. [ADR 0069](adr/0069-adopt-evolutionary-service-extraction.md) defines
the target business and consistency boundaries; the
[MSA transition roadmap](msa-transition-roadmap.md) defines the measurement, contract, conformance,
cutover, and rollback gates. Neither document authorizes a runtime or database split without a
separate extraction decision.

MSA Stage 1 has started under [ADR 0070](adr/0070-establish-optimization-worker-contract-boundary.md).
The optimization scheduler now depends on a process-neutral execution port, and versioned strategy,
market-data evidence, lease, heartbeat, and result-acceptance contracts exist for conformance work.
The authoritative adapter remains in-process; no shared database or second result writer is active.
New extracted computation/orchestration hosts default to F# while sharing the existing C# engine.
`StockTrader.ServiceContracts` is now an ASP.NET-independent library.

Optimization contract version 2 now carries the actual immutable prepared series required for
remote computation: normalized OHLCV bars, aligned ATR/close/protective-stop/cumulative-RSI arrays,
regime snapshots, and risk settings. A canonical data hash binds this payload to the evaluation
input, and malformed, duplicated, or tampered series fail lease validation. The executable transport
remains disabled by default. Evidence metadata has its own recomputed
identity as well, so changing provider, adjustment, calendar, or range claims without changing the
evidence ID is rejected.

[ADR 0071](adr/0071-extract-deterministic-strategy-kernel.md) starts that engine split. Strategy
documents, compilation, rule models, timeframe identity, and central strategy/indicator catalogs now
compile into package-free `StockTrader.Engine`; shared artifact validation lives in
`StockTrader.OptimizationProtocol`. Core indicator mathematics now also executes in the engine over
storage-independent `PriceBar` values, while `IndicatorService` is only an `OhlcvBar` adapter.
The indicator registry, condition operators, grouped rule aggregation, warmup policy, and
reference-symbol future-bar exclusion now execute in the engine as well. Long-position conservative
intrabar ordering, entry repricing, partial exits, stops, targets, scaling, and sizing also execute
there through one `EnginePriceBarMapper`. Execution-cost settlement and fixed/adaptive slippage now
also have one storage-independent engine owner; the monolith adapter only projects that result onto
its persisted trade model. Realized equity, daily-loss accounting, open-position mark-to-market,
equity-curve replacement, and peak drawdown now also execute in a storage-independent portfolio
ledger. Position orchestration and result aggregation still remain in the monolith, so the worker
uses the same Engine-owned full-period CAGR, Calmar, Sharpe, and Sortino policy as the monolith.
Trade-cycle aggregation and prepared-data transport still remain in the monolith, so the worker is
not yet an optimization-compute Pod.

[ADR 0072](adr/0072-deploy-shadow-optimization-worker.md) adds the first independent deployment
unit: a dedicated F# Optimization Worker image and Kubernetes Deployment/Pod in non-computing
`shadow` mode. It has its own ServiceAccount, probes, metrics, resource limits, security context,
and deploy scope, and mounts neither the monolith database nor a data volume. This proves the Pod
shell and rollback boundary but does not yet enable remote leases or claim Stage 2 computation
cutover. MSA completion requires
an independently built image and Kubernetes Deployment/Pod for each approved service boundary;
an extracted library or folder alone is not a deployed microservice.

The first shadow rollout evidence and rollback commands are recorded in the
[Optimization Worker operations note](../operations/optimization-worker-shadow.md).

[ADR 0073](adr/0073-authenticate-optimization-worker-control-plane.md) adds a separate fail-closed
workload authentication scheme and an outbound F# shadow probe. The Worker uses neither desktop
cookies nor a Kubernetes token; a cluster Secret is injected only into the API and Worker, while
the default application configuration keeps the transport disabled. This handshake does not yet
lease or execute jobs.

[ADR 0074](adr/0074-establish-durable-optimization-leases.md) adds Strategy Research-owned durable
lease records and authenticated claim, heartbeat, and idempotent result APIs. The F# Worker can
submit only an audited shadow result; it cannot write canonical optimization or financial results.
A separate `LeaseTransportEnabled=false` gate remains fixed in K3s because executable prepared data
requires internal TLS/workload identity before real-Pod activation.

[ADR 0075](adr/0075-extract-prepared-data-optimization-compute.md) adds the first complete service
calculation boundary. `StockTrader.OptimizationCompute` runs the existing Stage 1/Stage 2 search,
prepared-data simulation, ranking, and OOS semantics without a provider or database. The F# Worker
maintains and cancels leases while calling that shared C# computation instead of translating trading
rules. Dedicated compute tests pass from contract data alone, but K3s execution remains disabled
until TLS, real-Pod conformance, shadow-result comparison, and load/rollback gates pass.

[ADR 0076](adr/0076-secure-and-compare-shadow-optimization-compute.md) closes the executable
transport gate with an internal mTLS endpoint, client-certificate workload identity, the existing
shared secret as a second factor, and Worker egress restriction. Fresh unrestricted jobs now run in
both processes while only the in-process executor writes canonical results. Normalized exact result
hashes produce durable awaiting/match/mismatch audit states. Stage 2 candidate selection is also now
independent of database Job ID across synchronous, background, and Worker paths.

[ADR 0077](adr/0077-cut-over-remote-optimization-authority.md) defines the full Optimization Worker
completion boundary. Remote mode makes the Worker the exclusive candidate evaluator while Strategy
Research remains the single canonical database writer. Durable exactly-once commit, cancellation,
lease-generation reclaim, two-Pod bounded capacity, dependency-aware readiness, and an explicit
Shadow rollback are implemented. The production service-level K3s batch passed at `b636cb7`,
including two-Pod load, cancellation, Pod loss, lease reclaim, API restart, mTLS rotation and
preserved-generation rollback. ADR 0077 is Accepted and MSA Stage 2 is complete; Market Data is the
next service boundary.

[ADR 0078](adr/0078-extract-market-data-service.md) completes that Market Data boundary. The F#
service is the exclusive provider and normalized-bar authority in Remote mode, with its own SQLite
volume, correction revisions, evidence hashes, mTLS identity, provider credentials, probes,
metrics, backup/restore, and rollback projection. The production batch passed at `6587b65`; its
evidence is recorded in the
[Market Data cutover operations note](../operations/market-data-cutover.md). MSA Stage 3 is complete
and ML Training is the next extraction candidate.

## Target modules

| Module | Owns | Must not own |
| --- | --- | --- |
| Domain | Symbols, money, timeframe identity, strategy and order state | EF, HTTP, UI, configuration |
| Engine | Indicators, rule evaluation, fills, portfolio simulation, metrics | Database and broker access |
| Application | Preview, backtest, optimize, scan, evaluate, order use cases | Provider implementations |
| Infrastructure | SQLite, Alpaca, Yahoo, LS, notifications, system clock | Trading policy |
| API/Workers | Authentication, contracts, scheduling, compute composition | Duplicated strategy rules or direct financial writes |
| Desktop | Presentation and interaction | Duplicated backend catalogs |

Dependencies point inward. Infrastructure implements application ports; the domain never imports
infrastructure.

## Sources of truth

- Timeframe facts: one backend catalog.
- Provider capabilities, markets, lookback limits, and regime benchmark symbols: one backend
  provider catalog.
- Market identity, display name, time zone, and regular-session boundaries: one domain market
  catalog shared by provider metadata, calendars, scanning, synchronization, and scheduling.
- Provider regular-session request windows: one application policy converts a market-local date
  through the central calendar into DST-correct UTC boundaries for Alpaca and Yahoo.
- Historical provider request instants: one application policy preserves explicit UTC ranges,
  normalizes local inputs, and gives unspecified date boundaries deterministic UTC meaning.
- Market-regime trend evidence: one completed-bar policy with an explicit as-of cutoff, central
  200-day window, strict boundary semantics, and fail-closed insufficient-history behavior.
- ML signal-scoring evidence: one versioned causal feature catalog captured at signal time, one
  source-signal lineage through live positions and realized trades, and one chronological validation
  policy. Models without a matching manifest and content hash are not executable.
- ML market-regime evidence: one completed-bar feature schema shared by training and prediction,
  exactly four investor-facing cluster meanings assigned from training evidence, and one manifest
  binding the feature schema, complete label map, cluster count, and model hash. Arbitrary cluster
  numbers are never treated as financial meaning.
- ML operations: application-owned classifier, scorer, training, result, and immutable status
  contracts; one scoped training use case with constructor-injected ports; one process-wide run
  claim; and explicit generated API response contracts.
- Runtime risk state: one application-owned scoped use case over a purpose-specific evidence port;
  one process-wide immutable generation containing every account, portfolio, and fallback state;
  and no persistence, broker, cache, or service-location dependency inside risk calculations.
- Backtest and preview range policy: dedicated policy catalogs.
- Backtest period metrics: one unit-explicit application policy using the full evaluation range;
  trade activity dates never define CAGR, Calmar, Sharpe, or Sortino annualization.
- Indicator definitions: one registry that supplies calculation, validation, units, parameters,
  warmup, and supported timeframes.
- Strategy definitions: one typed aggregate compiled for preview, backtest, and live execution.
- Pattern identity, stable codes, investor-facing names, operational availability, and unavailable
  reasons: one domain catalog.
- Market symbol normalization and validation: one domain policy shared by research and live paths.
- Financial collection state: one application port; SEC symbol, interval, parsing, and ratio rules
  are deterministic policies outside workers and persistence adapters.
- Order execution modes and their operator-facing meaning: one domain catalog.
- Compiled custom-position instructions: one application resolver for close-rule and scaling-rule
  evaluation, canonical exit reason, profit input, execution-count state, and execution-session DTOs.
- Broker identity and operation-specific capabilities: one domain catalog used by account metadata,
  adapters, workers, and live execution guards.
- Broker balance positions: one purpose-specific application snapshot without EF strategy state or
  fabricated open timestamps; durable positions are created only from confirmed fill evidence.
- Streaming runtime: one validated operational settings section and one injected observation clock
  for reconnect timing, buffer flushing, watchlist synchronization, and activity staleness.
- LS provider timing: one deterministic KST token-expiry/rate-limit policy and injected clock shared
  by authentication, account/order adapters, and market-data boundaries.
- LS broker protocol: one request catalog and one tolerant response parser, with purpose-specific
  order, account, and order-history clients behind the broker facade. UTC evidence windows are
  translated to overlapping KST trading dates and then filtered by the exact requested interval.
- Live position order state: one application execution port for conditional claim, broker evidence,
  release, and atomic position/realized-trade fill commit.
- Open-position display state: one application query for holding duration, durable-order status,
  total unrealized PnL, and the observation instant shared by every API consumer.
- Pattern statistics and signal browsing: one storage-independent statistics query, one domain
  metric policy, and one deterministic signal filter/R/R/statistic-selection policy.
- Daily reporting: one deterministic schedule/window/projection policy, one activity read port, and
  isolated account-equity and notification adapters.
- REST intraday ingestion: one provider-bound application cycle; market-open and realtime fallback
  decisions use the effective provider rather than global US/KRX or streaming state.
- Dashboard reads: one storage-independent query reusing the risk/position observation, one focused
  activity store, one active-account projection, and an explicit generated desktop contract.
- Trade activity reads: one storage-independent recommendation/history query, one focused activity
  store, validated pagination, and explicit generated desktop contracts.
- Legacy signal/recommendation compatibility: deterministic EF-owned supersession preserves raw
  audit rows while every operational reader excludes repeated pre-identity activity.
- Signal actionability: one clocked policy and validated lifetime configuration shared by signal
  browsing, dashboard counts, and manual entry.
- Authentication state: one clocked application use case, a purpose-specific user store, and a
  best-effort audit sink backed by a separate append-only persistence port.
- Derived statistics timestamps: backtest statistics use the explicit result boundary, while live
  pattern-statistics refreshes sample one injected observation instant that persistence preserves.
- ML retraining schedule: one deterministic ET window policy owns weekday and DST behavior;
  interval, window, retry, failure, and cooldown values come from validated configuration.
- Stock-analysis responses: one explicit generated contract with central pattern names and
  unit-correct percentage presentation in the desktop.
- Live built-in configuration: one database-backed application service; entry detection and exit
  evaluation resolve the same persisted override snapshot and never rewrite application files.
- API contracts: explicit DTOs, later used to generate TypeScript types.
- Backtest result presentation: one explicit API response owns unit-preserving projections,
  cost-adjusted trade returns, intraday timestamps, and investor-facing performance breakdowns.
- Database shape: EF Core migrations; frozen legacy readers only adopt pre-migration databases.
- UI delivery: Svelte assets in the Desktop container; the API never serves application pages.
- Desktop page ownership: every retained page is reachable from the single navigation flow; legacy
  dashboard, signal, risk, and ML screens with no route are removed rather than maintained in parallel.
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
A multi-symbol fixture runs one compiled SMA-cross strategy over rising and falling symbols in the
same backtest, then compares each outcome with an isolated preview. This locks per-symbol indicator
cache isolation and prevents one symbol's calculation state from changing another symbol's trade.

Long-position execution now crosses a second named boundary in `Application/Execution`.
Pattern preview and backtest must delegate entry repricing and position state transitions to
`LongPositionExecutionSessionPolicy` instead of implementing private OHLC, realized-PnL, cost, or
scaling mutations. The session composes `LongPositionExecutionPolicy` and
`LongPositionScalingPolicy`, returning one ordered event stream and one canonical state. Because
OHLC data does not reveal intrabar
ordering, the policy deliberately uses the conservative sequence: the stop known at bar open,
then partial profit, target/strategy/time exit, and finally protective-stop updates that take
effect on the next bar. `LongPositionExitPolicyCatalog` is the sole owner of built-in defaults and
custom-strategy exit-policy construction for preview, backtest, and live monitoring. Live monitoring
projects a flat current-price snapshot through `LiveLongPositionExecutionAdapter` into the same
execution session. It submits only the first ordered execution intent and waits for a real broker
fill before applying quantity or partial-profit state, so simultaneous partial/target observations
cannot create overlapping orders. Snapshot parity fixtures compare bar-based and live decisions
where price ordering is fully observable. Built-in close rules such as cumulative RSI2
trend-break/threshold decisions also
live in pure execution policies rather than in backtest or worker adapters.
`LongPositionScalingPolicy` likewise owns original-entry-based share rounding, scale-in weighted
average price, adapter-supplied capital-cap enforcement, and scale-out remaining cost. The common
session owns post-fill execution counting and realized PnL. Preview and backtest only translate
session events into markers or trade records. `CompiledStrategyPositionInstructionResolver` is the
single owner of custom close-rule and scaling-rule interpretation for preview, backtest, and live
execution. The backtest adapter only selects the causal bar window and portfolio cap, so ordinary
held bars and NextOpen entry bars cannot silently use different exit or scaling rules.
Backtest scaling counts travel with each open position rather than living in processor memory, so
recreating an orchestration component cannot reset a rule's maximum-fill limit.
Live partial-profit fills use the same common-session share rounding and atomically move the
remaining stop to breakeven with the quantity reduction. Durable live position execution now also
stores scaling direction, rule index, weighted cost basis, and per-rule execution counts through
the same broker-evidence transaction. Custom live scaling uses the same compiled detector,
original-entry share rounding, persisted rule counts, and central scale-in capital cap as research.
If broker account equity is unavailable, scale-in capacity is zero while risk-reducing scale-out
instructions remain eligible.
`LivePositionExecutionEvaluator` owns live bar loading, ATR preparation, built-in indicator snapshots,
custom sell-rule evaluation, and translation into the shared decision policy.
`LivePositionMonitoringCycle` owns persistence, strategy loading, and durable order coordination,
partitioned by the position's stored account. Broker equity, prices, submission, and reconciliation
cannot cross account groups; only accountless legacy rows use the active account fallback. The
48-line `PositionExecutionManagerService` owns only market-session scheduling. Entry ATR period and
live-position lookback values come from `StrategyEvaluationPolicy`.
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
`PatternScannerService` is now only a channel, retry, and circuit-breaker adapter. A scoped
`ILivePatternScanCycle` owns symbol normalization, ET-date deduplication, provider context, daily
bars, cached benchmark regime, detection, and signal processing through purpose-specific ports.
The daily completion marker is written only after detection and signal processing succeed, so a
transient failure remains retryable while durable signal, recommendation, and entry claims prevent
duplicate financial effects.
The benchmark long-trend regime now has one deterministic owner in
`MarketRegimeTrendPolicy`. Preview, backtest, live scanning, stock analysis, and the ML base regime
all ignore future bars and use the same strict 200-day boundary. Insufficient benchmark history is
unknown and non-bullish instead of taking the former optimistic preview/backtest fallback.
Daily history synchronization follows the same split. `DailyDataSyncService` only starts the
initial recovery and configured periodic/retry loop; `IDailyMarketDataSyncCycle` selects one
provider-bound session and evaluates only that provider's typed market date and close window.
Initial recovery excludes the current unfinished daily bar. Scheduled synchronization overlaps the
last stored date, while SQLite OHLCV writes upsert the same bar identity so a later completed sample
corrects an earlier partial value instead of being discarded. Live scan deduplication also includes
the effective provider and its owned market date.
REST intraday ingestion now follows a matching provider-bound split. `MarketDataIngestionService`
owns only its injected-clock interval, retries, cooldown, and scoped invocation.
`IIntradayMarketDataIngestionCycle` resolves the effective provider before checking market or
realtime state, polls only that provider's regular market session, saves one successful batch before
publishing symbols, and surfaces a provider-wide outage to worker recovery. Alpaca streaming
connects only while Alpaca is selected. A provider switch rejects old callbacks, drains the buffered
batch, and exposes a transition state so Yahoo or LS REST polling cannot overlap the old stream.
Both ingestion modes publish scanner work only after persistence; failed stream flushes retain their
batch for serialized retry. `IRealtimeBarIngestionBuffer` owns callback admission and batching while
`IRealtimeBarBatchSink` owns the commit-then-publish adapter, leaving the Alpaca worker below 400
lines and free of repository or channel access.
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
are never blindly resubmitted. The intent also persists its requested quantity and partial-profit
meaning. A proven fill either reduces the remaining position or closes it, and the position update
plus its quantity-matched trade record commit atomically. A broker-reported quantity mismatch fails
closed for operator review instead of guessing.
`LivePositionExecutionCoordinator` owns submission and evidence-based reconciliation for full exits,
partial profit, scale-in, and scale-out orders. It rejects broker acknowledgements whose symbol,
direction, or quantity differs from the claimed intent. Background monitoring and the operator API
call the same use case. Position responses use one contract that
shows whether an exit is ready, missing a confirmed broker order ID, or awaiting broker resolution,
including the durable requested quantity;
the operator can request reconciliation but cannot force-clear an uncertain order.
Manual full exit, position-order reconciliation, and entry-order reconciliation now cross
`ILiveOrderManagement`. The use case routes persisted positions to their owning account instead of
the currently active account, while legacy account-less rows retain an explicit active-account
fallback. Disabled accounts may perform risk-reducing exits and durable reconciliation without
regaining permission for new entries. `OrderEndpoints` only binds explicit contracts and maps typed
outcomes to HTTP responses.

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

User settings now cross `SettingsManagementService` and `ISettingsManagementStore`. The HTTP
boundary accepts and returns explicit contracts instead of the EF `UserSettings` entity, validates
provider, order-mode, built-in-pattern, watchlist, and risk invariants before persistence, and uses
the injected application clock for modification time. Secret values are write-only through the API;
responses expose only configured-state booleans. `OrderMode` and its operator-facing labels live in
`Domain.Trading.OrderModeCatalog`, while provider and pattern choices are projected from their
existing domain catalogs. The desktop consumes those server choices and normalizes watchlist input
in a tested settings model rather than inventing fallback codes.

Background optimization delegates OOS boundaries, deterministic 60/40 search planning, restart
chunk positions, and duration checks to `OptimizationJobExecutionPolicy`. Both synchronous and
background optimization consume `OptimizationBacktestAssumptions`, so candidate rankings share one
slippage, commission, and cost-model baseline. Optimization workers receive `TimeProvider` instead
of reading system time. Both modes also call `IOptimizationCandidateEvaluator` for strategy variant
creation, timeframe data selection, prepared simulation, and candidate failure handling.
`OptimizationResultProjection` owns fractional-to-percent metric conversion for IS and OOS results;
the executor coordinates job lifecycle, chunks, persistence, and cancellation around that application
port. `IOptimizationEvaluationContextPreparer` now supplies both modes with the same resolved feed,
central market-regime benchmark, reference symbols, requested timeframe data, and risk settings.
`IOptimizationJobExecutionStore` isolates pause/cancel observation, chunk checkpoints, ranked-result
storage, legacy parameter JSON, and OOS-only updates. Its SQLite adapter is the only owner of
`OptimizationResult` mapping; the executor is now 329 lines and capped below 350 lines.
`IOptimizationJobLifecycle` likewise owns Pending selection and Running, Completed, Cancelled,
shutdown-Pending, and Failed transitions. It supplies a storage-independent execution ticket, so
neither optimization background component imports `Data` or `Models`; the polling worker is capped
below 200 lines. Pending selection is claimed with a status-guarded database update, so concurrent
workers cannot both start the same job. Ranked result merging and its following progress checkpoint
commit in one SQLite transaction; a restart therefore observes either the whole completed chunk or
none of it. User pause, resume, and cancel commands also cross `OptimizationJobControlService` and a
status-guarded persistence port. Their legal transitions live in an application policy, concurrent
commands cannot overwrite each other, and startup recovery uses the same purpose-specific boundary.
Creation, list/detail projection, settings updates, result reads, and terminal deletion now cross
`OptimizationJobManagementService` and `IOptimizationJobManagementStore`. The 212-line endpoint
module contains no persistence entity, repository, result JSON, combination-count formula, or job
projection math. The SQLite adapter returns the stored result ID explicitly, so selecting “apply
this result” identifies the chosen row instead of silently falling back to the automatic candidate.
Manual and automatic result promotion now cross the scoped `OptimizationAutoTuneService` and
`IOptimizationAutoTuneStore`. Candidate eligibility and IS/OOS ranking are pure application policy;
persisted request/parameter JSON remains in the SQLite adapter. Apply counts use an atomic database
increment, and continuous-job result deletion plus reset commit in one transaction.
The former `IOptimizationRepository` pass-through has been removed. Execution checkpoints, queue
lifecycle, operator controls, administration, and promotion now each terminate at their own SQLite
adapter; no production component can reach a catch-all optimization persistence API.
Built-in pattern discovery and construction now use `BuiltInPatternDetectorCatalog` in both runtime
DI and backtesting. The catalog covers every non-custom `PatternType`, including TQQQ 200-SMA, and
the same factory applies baseline or request-override settings without a second constructor list.
`PatternType` itself and its stable code, investor-facing display name, and built-in support flag
are owned by `Domain.Strategies.PatternCatalog`. Stable enum names and numeric values remain the
persistence/API contract; notifications and strategy-builder metadata consume the central display
metadata instead of maintaining channel-specific labels. Custom recommendations display their
actual stored strategy name when one is available.
Signal timestamps now distinguish the evaluated market bar from the live observation time. Pattern
detectors are deterministic; the live scan boundary alone owns the observation clock, and persisted
signals are idempotent per strategy and bar.
Symbol-profile assignment now crosses `SymbolProfileManagementService` and
`ISymbolProfileStore`. The API and live scanner no longer read EF profiles directly; validation,
defaults, active-profile selection, and modification time have one application owner.

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
- `adr/0009-deterministic-optimization-job-policy.md`: keep search planning, restart semantics,
  clocks, and candidate execution assumptions deterministic across optimization modes.
- `adr/0010-share-optimization-candidate-evaluation.md`: route synchronous and background candidate
  simulation plus result-unit projection through one application boundary.
- `adr/0011-prepare-optimization-evaluation-context.md`: resolve feed identity, regime benchmark,
  prepared symbols, timeframe data, and risk settings through one optimization preparation port.
- `adr/0012-isolate-optimization-job-execution-store.md`: keep job checkpoints, result JSON,
  persisted control signals, and OOS-only updates behind an application storage port.
- `adr/0013-isolate-optimization-job-lifecycle.md`: map persisted jobs to application execution
  tickets and centralize queue/status transitions behind one lifecycle port.
- `adr/0014-commit-optimization-chunks-atomically.md`: claim queued work once and commit each result
  chunk with its restart checkpoint.
- `adr/0015-control-optimization-jobs-conditionally.md`: own user control transitions in the
  application layer and persist them with status-guarded updates.
- `adr/0016-isolate-optimization-job-management.md`: separate job administration and projections
  from EF entities and preserve result identity across the HTTP boundary.
- `adr/0017-isolate-optimization-auto-tune.md`: keep result-promotion policy independent from
  persistence and make apply metadata and continuous recycling atomic.
- `adr/0018-remove-broad-optimization-repository.md`: make each purpose-specific optimization
  adapter own its EF queries and transactions, then remove the catch-all repository.
- `adr/0019-centralize-built-in-pattern-detectors.md`: use one detector inventory for live scanning,
  analysis, backtesting, walk-forward evaluation, and optimization.
- `adr/0020-isolate-stock-recommendation-policy.md`: keep recommendation formulas deterministic,
  move indicator snapshot composition behind a focused boundary, and configure analysis operations.
- `adr/0021-own-market-data-identity-in-domain.md`: make Domain the sole owner of timeframe and data
  provider identity while preserving persisted integer and JSON enum compatibility.
- `adr/0022-register-rule-indicator-calculators.md`: bind every central indicator descriptor to one
  runtime calculator and split evaluation caching, dispatch, categories, and math.
- `adr/0023-own-pattern-identity-and-display-metadata.md`: preserve pattern identity compatibility
  while centralizing investor-facing names and built-in support metadata in Domain.
- `adr/0024-isolate-settings-management.md`: move settings validation and mutation behind an
  application use case, keep secrets write-only, and drive desktop choices from domain catalogs.
- `adr/0025-separate-signal-event-and-observation-time.md`: keep research signals deterministic
  while preserving live freshness and per-bar persistence identity.
- `adr/0026-isolate-symbol-profile-assignment.md`: route profile validation, activation, API
  contracts, and live selection through one application boundary.
- `adr/0027-isolate-live-signal-evaluation-state.md`: project live trade history, portfolio counts,
  daily executions, and sector data through one persistence-independent application snapshot.
- `adr/0028-isolate-research-universe-queries.md`: move universe ranking, financial-factor math,
  import normalization, and explicit response contracts behind one research application boundary.
- `adr/0029-isolate-financial-collection-boundary.md`: move import-run state and ticker projections
  behind an application port and make SEC interpretation and financial arithmetic deterministic.
- `adr/0030-isolate-trading-account-boundary.md`: centralize broker metadata, isolate account
  persistence and broker construction, and keep credentials out of account responses.
- `adr/0031-unify-live-entry-execution.md`: bind account and broker selection, retain entry-order
  evidence, and route automatic and manual entries through one atomic local execution boundary.
- `adr/0032-durable-entry-execution-lifecycle.md`: claim entries before broker submission and
  reconcile uncertain outcomes without permitting duplicate retries.
- `adr/0033-centralize-broker-capabilities.md`: expose operation-specific broker support and reject
  unsupported live actions before they mutate durable execution state.
- `adr/0034-isolate-live-position-execution-store.md`: remove live position execution from the broad
  trade repository and commit fills through an isolated atomic persistence adapter.
- `adr/0035-retire-broad-trade-repository.md`: split remaining trading reads and writes across four
  focused ports, isolate EF contexts, and remove mutable open-position caching.
- `adr/0036-isolate-live-order-management.md`: move operator exit and reconciliation orchestration
  behind one use case and route every persisted position through its owning account.
- `adr/0037-isolate-risk-overview-and-clock.md`: move risk-screen projection behind one application
  query, inject observation time, and count legacy accountless positions exactly once.
- `adr/0038-isolate-portfolio-performance.md`: move open-position and performance reads behind
  application projections and calculate drawdown from initial account equity over complete history.
- `adr/0039-isolate-signal-statistics-queries.md`: centralize pattern-statistics metrics and resolve
  signal statistics by exact symbol without endpoint persistence or formula ownership.
- `adr/0040-isolate-daily-report-use-case.md`: isolate report scheduling, DST-safe market-day
  windows, activity reads, equity access, and notification publication behind application ports.
- `adr/0041-isolate-dashboard-query.md`: replace endpoint orchestration and fabricated desktop risk
  values with one explicit storage-independent dashboard read model.
- `adr/0042-centralize-live-pattern-configuration.md`: make executable pattern availability and
  persisted live parameters one shared runtime source.
- `adr/0043-isolate-trade-activity-query.md`: move recommendation/history projection and validated
  pagination behind a storage-independent application read boundary.
- `adr/0044-type-stock-analysis-contract.md`: replace the anonymous analysis response and
  casing/unit fallbacks with one generated desktop contract.
- `adr/0045-supersede-legacy-activity-duplicates.md`: preserve pre-identity activity rows while
  excluding deterministic same-day duplicates from operational reads.
- `adr/0046-centralize-signal-actionability.md`: use one configured observation-time window for
  signal browsing, dashboard counts, and manual execution.
- `adr/0047-isolate-authentication-and-audit.md`: isolate authentication state, atomic lockout
  transitions, observation time, and best-effort security audit persistence.
- `adr/0048-make-statistics-and-ml-scheduling-deterministic.md`: make derived statistics timestamps
  explicit and re-anchor configured ML retraining windows across weekends and DST.
- `adr/0049-separate-broker-snapshots-and-streaming-clock.md`: prevent broker balance snapshots from
  masquerading as durable positions and make streaming timing explicit and configurable.
- `adr/0050-explicit-persistence-and-notification-clocks.md`: require application-owned persistence
  timestamps and injected notification clocks instead of hidden process time.
- `adr/0051-route-live-position-monitoring-by-account.md`: partition automatic position monitoring
  by durable account ownership and isolate the worker from the monitoring cycle.
- `adr/0052-remove-account-ambiguous-order-cancellation.md`: remove the unused order-ID-only
  cancellation surface and require durable account ownership before any future cancellation use
  case can reach a broker adapter.
- `adr/0053-isolate-live-entry-reconciliation-cycle.md`: keep the hosted worker as a clocked
  scheduler while a purpose-specific cycle owns account-grouped entry reconciliation and isolates
  provider failures.
- `adr/0054-isolate-live-pattern-scan-cycle.md`: keep the scanner worker as channel/recovery plumbing
  while a provider-aware application cycle owns daily detection and completion state.
- `adr/0055-isolate-provider-market-daily-sync.md`: bind daily completion windows to the effective
  provider market and replace stale partial OHLCV through overlapping upsert synchronization.
- `adr/0056-isolate-provider-market-intraday-ingestion.md`: bind REST minute-bar polling and realtime
  fallback to the effective provider and make worker recovery observable.
- `adr/0057-centralize-backtest-period-metrics.md`: calculate annualized backtest metrics from the
  explicit evaluation period rather than trade activity dates.
- `adr/0058-type-backtest-result-contract.md`: project unit-preserving backtest results through one
  generated API and desktop contract.
- `adr/0059-centralize-market-regime-trend.md`: give completed-bar benchmark trend evidence one
  deterministic, fail-closed owner across preview, backtest, live, analysis, and ML.
- `adr/0060-isolate-ls-broker-protocol.md`: isolate current LS request shapes, tolerant response
  parsing, and exact UTC-to-KST order-history evidence behind purpose-specific clients.
- `adr/0061-require-causal-signal-scoring-evidence.md`: train and validate signal scoring only from
  entry-time features linked to completed position outcomes, with versioned model artifacts.
- `adr/0062-bind-market-regime-model-to-cluster-meaning.md`: bind completed-bar regime features and
  the full four-cluster investor meaning map to the exact executable model artifact.
- `adr/0063-make-ml-training-an-application-use-case.md`: move ML contracts inward, remove scoped
  service location, preserve one global training claim, and expose atomic generated status contracts.
- `adr/0064-publish-risk-state-as-one-application-generation.md`: move runtime risk management
  inward, isolate broker and persistence evidence, and atomically publish multi-account state.
- `adr/0065-unify-compiled-position-instructions.md`: route preview, backtest, and live custom close
  and scaling conditions through one compiled-strategy instruction resolver.
- `adr/0066-centralize-provider-session-windows.md`: derive dated provider requests from the central
  market calendar and one DST-correct regular-session window policy.
- `adr/0067-normalize-provider-request-instants.md`: preserve historical request instants across
  developer and server time zones through one validated UTC interval policy.
- `adr/0068-state-market-data-evidence-explicitly.md`: replace the weekend-only calendar with
  versioned exchange holiday and early-close evidence, own per-provider price adjustment in a
  catalog, and carry the resulting run conditions into persisted result metadata.
- `refactoring-roadmap.md`: migration order, gates, and measurable completion criteria.
