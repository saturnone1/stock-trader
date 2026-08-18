# Atomic application-owned runtime risk state

Runtime risk management now uses an application-owned scoped use case with constructor-injected
evidence. Account, broker, settings, position, and cache access remain in a focused adapter instead
of being located through nested dependency-injection scopes. The position-cache duration is now a
validated `Trading` option instead of a code constant.

Each refresh publishes account, portfolio, and fallback risk as one immutable generation. Readers
can no longer observe account values from a new refresh alongside a portfolio value from an older
refresh. Broker-reported zero daily PnL remains authoritative, and accountless legacy positions are
still counted exactly once.

When no account is enabled, current open-position loss now updates the portfolio risk snapshot. The
old implementation updated only a fallback field and could leave a stale portfolio value visible.
This operational correction does not change strategy, indicator, backtest, or execution formulas.
