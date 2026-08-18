# Account-safe order lifecycle

- Removed an unused internal cancellation method that selected the active account from a broker
  order ID alone.
- Existing operator exits and reconciliation continue to use the durable position or recommendation
  owner and fail closed when the account is missing or the symbol is ambiguous across accounts.
- Broker cancellation capability remains available for a future account-qualified cancellation
  use case; no public API or desktop behavior changed.
- Added an architecture guard preventing order-lifecycle cancellation from falling back to the
  currently active account.
