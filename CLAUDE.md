# StockTrader contributor guide

`AGENTS.md` and `docs/architecture/` are the authoritative engineering rules. This file exists
only as a compatibility entry point for tools that look for `CLAUDE.md`.

## Active application

- `desktop-app/` is the only UI; it is a Svelte application.
- The .NET process is a JSON API plus background workers. Do not add Blazor, Razor component,
  MudBlazor, or other server-rendered UI dependencies.
- Preview, backtest, and live execution must preserve the trading invariants in `AGENTS.md`.

## Required checks

```text
dotnet build StockTrader.csproj --no-restore
dotnet test tests/StockTrader.Tests/StockTrader.Tests.csproj --no-restore
cd desktop-app && npm test && npm run build
```

## Supported operations

- Local containers: `docker compose up --build`
- K3s production: `scripts/deploy-k3s.sh [release-tag]`
- API health: `/api/health`
- K3s manifests: `k8s/deployment-api.yaml` and `k8s/deployment-desktop.yaml`
- Secrets: create `stocktrader-alpaca` from `k8s/secret.example.yaml`; never commit real values.

Preserve unrelated work, use EF Core migrations for new schema changes, and keep endpoint and
worker code free of trading calculations.
