# Preview and backtest agree on entry geometry and warmup boundary

Two disagreements between the preview and backtest engines are corrected. Both change preview
results in the affected cases; neither changes backtest results.

## Fallback target multiple

When a signal carries no usable target price — its target is at or below the entry — the fill policy
falls back to a fixed R multiple. Preview derived that multiple from the strategy's own
`AtrTargetMultiplier / AtrStopMultiplier`, while the backtest's next-open fill hardcoded 2R. The
same signal on the same strategy therefore produced different target prices in research and preview.

`LongEntryFillPolicy.ResolveFallbackTargetMultiple` now owns the derivation and both paths use it.
The backtest captures the value at signal time and applies it at the fill bar, so a deferred
next-open entry keeps the geometry its strategy declared. A strategy with a 2 ATR stop and a 3 ATR
target now falls back to 1.5R in both engines rather than 1.5R in preview and 2R in backtest.

## Warmup boundary

The backtest does not evaluate a bar whose index is below `MinimumWarmupBars`. Preview started one
bar earlier, at `MinimumWarmupBars - 1`. A preview could therefore show an entry that the backtest
would never take, and because entry timing feeds re-entry and circuit-breaker cooldowns, a single
extra entry shifted every subsequent decision in that run.

Preview now uses the same boundary. This is the more conservative direction: it requires more
warmup rather than less, and it leaves every existing backtest result unchanged. The affected
golden previously recorded three entries and one open position; it now records two entries, both
closed, and a new golden pins the shared boundary directly.

Note that the boundary is deliberately aligned rather than re-derived. `MinimumWarmupBars = 50`
could be read as making index 49 the first bar with fifty bars of history, which would mean the
backtest is one bar too strict. Loosening the backtest instead would have changed every stored
backtest result, so the engines were aligned on the stricter existing behavior. Revisiting which
index is semantically correct is a separate decision.
