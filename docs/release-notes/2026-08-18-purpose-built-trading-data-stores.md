# Purpose-built trading data stores

Trade history, open positions, recommendations, and pattern signals now use four focused
application contracts backed by isolated SQLite contexts. The previous catch-all trade repository
and its unused mutation methods have been removed.

Parallel dashboard and history reads no longer share one EF context. Open positions are read fresh
instead of caching mutable entity instances, preventing partially changed position state from being
observed across requests. Live entry and exit state transitions remain in their dedicated atomic
execution stores.
