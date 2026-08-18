# Account-owned live order management

Manual position exits and order reconciliation now run through one live-order management use case.
The previous API implementation selected the currently active broker even when the persisted
position belonged to a different account. It now routes by the position's durable account ID and
uses the active account only for legacy positions that have no account identity.

Disabled accounts still reject new entries, but an already-open position may be closed to reduce
risk and an uncertain order may be reconciled. Duplicate same-symbol positions continue to fail
closed until an account-specific operator action is available. Order responses now use explicit API
contracts generated into the desktop client schema.
