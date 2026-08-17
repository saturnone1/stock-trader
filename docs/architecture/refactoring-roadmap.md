# Refactoring roadmap

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
