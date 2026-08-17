# StockTrader agent guide

## Project status

This is an active, production-deployed trading application. The canonical user interface is
`desktop-app/` (Svelte). `Components/` is legacy Blazor UI and must not receive new features.

Read `docs/architecture/README.md` before structural work. Architectural decisions live in
`docs/architecture/adr/` and the staged refactoring plan lives in
`docs/architecture/refactoring-roadmap.md`.

## Dependency direction

The target is a modular monolith with this one-way dependency flow:

`Api/BackgroundServices -> Application -> Engine/Domain <- Infrastructure`

During the staged migration, new code must follow these rules even while the folders still
compile into one project:

- Domain and deterministic engine code must not depend on EF Core, ASP.NET, HTTP clients,
  configuration providers, system time, or broker SDKs.
- API endpoints and background services are adapters. They call application use cases and do
  not implement trading rules.
- Alpaca, Yahoo, LS Securities, SQLite, email, and operating-system concerns remain adapters.
- Prefer purpose-specific ports over generic repositories or a new mediator framework.
- A policy or catalog has one owner. Do not duplicate timeframe, indicator, strategy, or data
  provider metadata in another feature.

## Trading invariants

- Preview, backtest, and live trading must use the same compiled strategy semantics.
- Completed-bar strategies cannot read future bars. Next-open orders cannot skip their entry bar.
- If intrabar price order is unknowable, simulation chooses the conservative fill.
- Costs affect portfolio equity when the execution occurs.
- Timeframe, adjustment mode, market calendar, and warmup requirements are explicit inputs.
- Live trading rejects features whose backtest/live execution parity is not implemented.
- Any intentional change to historical results requires a named characterization test and a
  release note explaining why the old result was wrong.

## Hardcoding policy

- Mathematical and protocol invariants stay in code with a named constant and explanation.
- Operational values use validated typed options.
- Timeframe facts, indicator metadata, strategy metadata, and provider capabilities live in
  central catalogs.
- Secrets come only from user secrets, environment variables, or Kubernetes secrets.
- Frontend controls obtain shared catalogs from API metadata instead of copying backend lists.

## Required verification

Run these commands after relevant changes:

```text
dotnet build StockTrader.csproj --no-restore
dotnet test tests/StockTrader.Tests/StockTrader.Tests.csproj --no-restore
cd desktop-app && npm run build
```

For user-visible API or desktop changes, build and import the K3s images, roll out only the
affected deployments, then verify `/api/health`, the desktop URL, pod status, and startup logs.

## Change discipline

- Preserve unrelated work in dirty worktrees.
- Refactor through tested seams; do not rewrite the trading engine in one change.
- Keep compatibility readers during database or strategy-document migrations.
- New architectural boundaries require an ADR and an architecture test where enforceable.
- Keep `Program.cs` as a composition root; schema migration and business behavior belong elsewhere.
