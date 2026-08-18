# Correct dashboard risk metrics

- The dashboard no longer displays a hardcoded 0% “Total Exposure”.
- The dashboard no longer relabels a negative daily return as “Max Drawdown”. That value was not a
  drawdown calculation and was therefore misleading.
- The risk card now shows actual daily PnL, daily return, unrealized PnL, and the real trading-halt
  state supplied by the backend risk service.
- Account balance, buying power, positions, active-signal count, recent recommendations, market
  regime, and order mode now come from one explicit generated API contract.
- Open-position return and recommendation R/R calculations have named central policy owners.
