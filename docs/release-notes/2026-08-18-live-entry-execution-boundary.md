# Live entry execution boundary

- Automatic and manual entries now use the same broker-submission, fill-confirmation, repricing,
  account-ownership, and local-persistence coordinator.
- Broker entry submission returns order evidence instead of a boolean.
- Terminal rejection states and mismatched symbol, direction, or quantity evidence fail closed before
  local position creation.
- The account and broker are resolved as one snapshot, preventing an active-account switch from
  assigning the position to a different account after submission.
- Recommendation execution state and the new position are committed in one database transaction.
- A broker-accepted order whose local tracking fails is reported as accepted with a do-not-retry
  warning rather than as a rejected order.
- Rejected manual orders now retain an unexecuted recommendation audit record. No position or
  executed marker is written.
