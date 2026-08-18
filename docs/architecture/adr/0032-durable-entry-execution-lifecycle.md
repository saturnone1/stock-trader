# ADR 0032: Persist and reconcile the live-entry lifecycle

## Status

Accepted

## Context

ADR 0031 unified automatic and manual entry, but its local state was written only after broker
acceptance and position confirmation. A process failure between submission and the local commit
could lose the broker order ID and permit the same signal to create another recommendation and
another order after restart. A transport exception also cannot prove whether a broker rejected or
accepted the request.

## Decision

- A persisted recommendation is conditionally claimed before any broker side effect. The claim fixes
  the request time and trading account and permits only one worker to submit that recommendation.
- The returned broker order ID is written immediately. A transport exception retains the claim as
  `SubmissionUnconfirmed`; it is never treated as a safe rejection or automatically retried.
- Reconciliation uses the stored order ID when available. Without an ID, symbol, long direction,
  quantity, and request time must identify exactly one broker order. Missing evidence waits;
  ambiguous or mismatched evidence fails closed for operator review.
- Only a final filled order with an exact quantity and positive average fill price can atomically mark
  the recommendation executed and create the local position. A proven rejected, cancelled, or
  expired order releases the claim while retaining its account, order ID, and failure note for audit.
- `PatternSignal.Id` is stored as the recommendation's nullable, unique `SourceSignalId`.
  Recommendation persistence reuses the existing row and original trading parameters for repeated
  automatic or manual handling of the same signal. Signal batch persistence restores IDs for
  already-known signals after a process restart.
- A hosted reconciliation loop groups pending recommendations by their fixed account, reads broker
  history, and invokes the same coordinator used by the operator endpoint. Disabled accounts still
  allow this read-only recovery path while remaining unavailable for new orders.
- The recommendation screen exposes 주문 전, 접수 확인 필요, 체결 대기, 체결 반영 완료,
  and 주문 실패 states and permits an authenticated immediate reconciliation request.

## Consequences

A restart or network interruption cannot silently turn an uncertain submission into a duplicate
entry. The system may intentionally leave an entry pending when broker evidence is missing or
ambiguous; safety takes precedence over automatic retry. Operators can see and recheck that state,
while exact fills recover automatically. The schema gains durable entry lifecycle columns, a pending
query index, and a filtered unique signal identity index through an EF migration.
