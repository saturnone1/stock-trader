# ADR 0060: Isolate the LS broker protocol boundary

## Status

Accepted

## Context

The LS broker adapter combined order submission, cancellation, account and position reads, order
history, HTTP transport, and JSON interpretation in one 523-line class. Protocol identifiers and
request fields were embedded in each operation. The adapter still sent legacy order and cancel TR
codes and an obsolete balance request shape.

Order-history reconciliation also treated a UTC date as a Korean trading date. An interval crossing
Korean midnight could omit one required day, while every row returned for a requested day was
accepted even when its actual broker timestamp fell outside the caller's interval. Both errors can
change which live order evidence is reconciled.

## Decision

- `LsSecuritiesBrokerService` remains the `IBrokerService` facade and delegates to purpose-specific
  order, account, and order-history clients.
- `LsBrokerProtocol` is the single owner of LS paths, current TR codes, request block names, and
  request factories. Current cash-order and cancel requests use `CSPAT00601` and `CSPAT00801`.
- The `t0424` position request uses the documented `prcgb`, `chegb`, `dangb`, `charge`, and
  `cts_expcode` fields.
- `LsBrokerResponseParser` accepts numeric values represented as either JSON numbers or strings.
  It reads the current order response block and retains the legacy response block as a compatibility
  reader only.
- `LsOrderHistoryWindow` converts the exact UTC interval to every overlapping KST date. Returned
  rows are parsed from their actual `OrdDt` and `OrdTime` evidence and included only when the UTC
  timestamp is inside the inclusive requested interval.
- Caller cancellation is rethrown; provider and parsing failures remain adapter-level failures and
  do not escape as broker SDK or JSON types.

## Consequences

Protocol upgrades and response-shape changes now have one focused owner and direct characterization
tests. The facade is capped at 150 lines so orchestration cannot absorb protocol parsing again.

Historical order queries near KST midnight can intentionally return different results. The old
result was wrong because it queried UTC calendar dates as Korean dates and admitted rows outside the
requested evidence interval. This correction improves live reconciliation safety without changing
the compiled strategy or simulation engine.
