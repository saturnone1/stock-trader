# Preview and backtest share one bar-step semantics

Four remaining disagreements between the preview and backtest engines are resolved. The same
compiled strategy on the same bars now advances through a bar the same way in both.

## Warmup boundary — corrected in the backtest

Evaluating the bar at index `i` has `i + 1` bars of history available, so requiring
`MinimumWarmupBars` means index `MinimumWarmupBars - 1` is the first evaluable bar. The backtest
used the bar count directly as an index floor and therefore skipped one bar more than it needed to,
requiring 51 bars where 50 were specified.

`StrategyEvaluationPolicy.FirstEvaluableBarIndex` now owns the boundary and both engines use it.
This is the opposite of the direction taken in the previous release note, which aligned preview onto
the backtest's stricter value; the backtest was the one that was wrong, and it has been loosened
rather than the preview tightened. A backtest may now open a position one bar earlier than before
where a signal fires on that first bar.

## Evaluation window

Preview passed the entire bar prefix to the strategy runtime while the backtest bounded the window
to `SimulationWindowBars` (260 daily, 800 intraday). Any indicator sensitive to window length — a
cumulative count, a full-range high or low — could therefore read a different value on the same bar
in each engine. Preview now uses the same bound.

## Next-open entry re-validation

Preview committed a next-open entry at the *signal* bar: it materialized the position immediately
with an entry index pointing at the following bar. The backtest holds a pending entry and
re-validates it at the *fill* bar, so a circuit breaker or re-entry cooldown that trips between the
two bars blocks the fill. Preview honored no such block — the entry was already decided.

Preview now holds the same pending state, re-checks entry eligibility at the fill bar, and reprices
against that bar's open. The fill runs before exit evaluation in the same iteration, so entry-bar
exit and scaling rules continue to apply exactly as before.

## Drawdown observation timing

The backtest updates strategy realized equity and observes drawdown on every settled execution,
including partial exits and scale-outs. Preview observed drawdown only when a position closed
completely, so a max-drawdown circuit breaker that should have tripped mid-position stayed inactive
and preview kept taking entries the backtest would have blocked.

Preview now observes drawdown on each realization. Compounded return is still folded once per
completed cycle, because compounding partial realizations separately would not equal compounding
the cycle as a whole; the drawdown observation uses its own realized-equity index, mirroring how the
backtest keeps strategy realized equity separate from the portfolio equity curve.
