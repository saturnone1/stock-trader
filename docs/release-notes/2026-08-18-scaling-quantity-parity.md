# Scaling quantity parity correction

Pattern preview and backtest now calculate scale-in and scale-out quantities through the same
deterministic execution policy. Fractional shares are conservatively rounded down from the original
entry quantity, with a one-share minimum, and both paths use the same weighted-average entry and
remaining-cost calculations.

Previously preview rounded fractional quantities to the nearest whole share while backtest truncated
them. Backtest could also change the percentage basis after a partial exit because the remaining
quantity replaced the original entry quantity. Those differences could produce different scale
fills, average prices, and returns for the same compiled strategy.

Scaling-condition evaluation no longer consumes a rule's maximum execution count before a fill is
possible. A capital-capped scale-in or an impossible scale-out leaves the count available for a
later bar.

Backtest execution counts are now owned by each open position and survive position-state copies or
recreation of the orchestration processor. They are no longer kept in a separate symbol dictionary
whose lifetime could silently reset a rule's maximum-fill limit.

Live execution continues to reject strategies containing scale-in or scale-out rules until broker
order submission, partial-fill reconciliation, and persisted scaling state provide the same
semantics. Golden tests lock this fail-closed boundary.
