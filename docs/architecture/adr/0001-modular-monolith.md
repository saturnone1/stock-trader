# ADR 0001: Use a modular monolith

- Status: Accepted
- Date: 2026-08-18

## Context

Trading behavior is coupled across API endpoints, background workers, preview, backtest, live
orders, persistence, and two user interfaces. Several central files exceed one thousand lines and
repeat timeframe and indicator policy. Splitting this system into network services would add
failure modes without first fixing those internal boundaries.

## Decision

Keep one backend deployment and introduce explicit Domain, Engine, Application, Infrastructure,
and Host boundaries. Begin as folders and namespaces, then split assemblies only after dependency
tests prove the boundaries are stable.

The deterministic strategy engine becomes the shared semantic core for preview, backtest, and live
trading. External systems remain replaceable adapters.

## Consequences

- Refactoring can proceed in small deployable increments.
- Strategy parity is testable without a database or network.
- AI tasks have a smaller reasoning radius and machine-discoverable ownership.
- Some temporary adapter code will exist during migration.
- Project splitting is delayed until it prevents, rather than merely describes, bad dependencies.
