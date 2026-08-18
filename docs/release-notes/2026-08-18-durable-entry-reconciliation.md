# Durable live-entry reconciliation

New stock entries now record a durable intent before contacting the broker. The selected account,
request time, broker order ID, and any reconciliation warning survive an application restart.

Automatic and manual entry both use the same conservative recovery rules. Only an exact final fill
creates a local position. A confirmed rejection permits retry, while a network interruption, missing
order, ambiguous match, or mismatched fill remains blocked to prevent duplicate or incorrect trades.

Repeated processing of the same pattern signal reuses one recommendation through a database-backed
signal identity. This also protects rapid repeated clicks on the manual-order action.

The recommendation screen now shows the operational entry state in Korean and offers a button to
check the broker immediately. A background service performs the same reconciliation automatically,
including for an account disabled after its order was submitted.
