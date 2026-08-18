# ADR 0051: Route live position monitoring through the durable owning account

## Status

Accepted

## Context

Durable positions store the `TradingAccount.Id` that received the confirmed entry fill. Manual exits
and operator reconciliation already resolved that account, including disabled accounts that must
remain available for risk reduction. The background position worker instead loaded every open
position, selected the currently active broker once, and used that broker's equity, holdings, prices,
and order history for the whole set. After an active-account change, a position owned by another
account could therefore be evaluated from the wrong price snapshot or submitted to the wrong broker.

The worker also combined host scheduling, persistence reads, account routing, strategy loading,
evaluation, submission, reconciliation, and notification in one component. Its polling and immediate
order-resolution values were embedded in code.

## Decision

- `PositionExecutionManagerService` is a scheduling adapter. It checks the market session, creates a
  scope, and invokes the application-facing `ILivePositionMonitoringCycle` contract.
- `LivePositionMonitoringCycle` partitions positions by their durable `AccountId`. Pending orders use
  the owning account's reconciliation context; positions eligible for a new risk-reducing order use
  the owning account's position-exit context.
- Only legacy positions whose `AccountId` is zero use the active account, as an explicit compatibility
  path.
- Broker equity, holdings, and symbol prices are loaded separately for each account group. An
  unavailable owning account fails closed for that group and cannot fall through to the active broker.
- A disabled owning account remains available for reconciliation and risk-reducing exits, but cannot
  receive a scale-in order.
- A position that was pending at cycle start is never evaluated again in the same cycle, even when
  reconciliation completes and clears its pending state.
- Monitoring interval, resolution attempts, and resolution delay are startup-validated
  `TradingSettings` values. Worker and resolution delays use the injected `TimeProvider`.

## Consequences

Switching the active account cannot redirect existing positions or mix one account's equity and price
snapshot into another account's strategy decision. Disabled but stored owning accounts remain usable
for reconciliation and risk reduction without permitting exposure increases. The worker is reduced
to a small host adapter, while account routing is directly testable without starting a hosted service.
There is no database schema or public
HTTP contract change.
