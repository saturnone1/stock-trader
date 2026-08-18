# Live position execution store boundary

Live full exits, partial profits, scale-ins, and scale-outs now persist through a dedicated atomic
execution store. The order coordinator no longer depends on the broad trade repository or creates a
database trade entity.

The SQLite adapter retains the existing compare-and-set claim, broker order evidence, restart
reconciliation, fill validation, weighted-price update, scaling counter, and realized-trade behavior.
Each operation uses an isolated database context, and a fill plus its realized trade commit in one
transaction.
