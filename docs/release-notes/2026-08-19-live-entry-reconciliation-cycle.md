# Isolated live entry reconciliation

- Split pending-entry reconciliation from its background scheduler into a directly testable cycle.
- Pending entries continue to use their durable owning accounts; missing account evidence now emits
  a critical diagnostic and never falls back to the active account.
- A broker order-history failure for one account no longer delays reconciliation for healthy
  accounts in the same cycle.
- Reconciliation scheduling now uses the injected clock, and the supported interval is validated at
  startup instead of silently changing configured values.
- Added account-isolation, failure-isolation, invalid-ownership, clock-boundary, and architecture
  regression coverage.
