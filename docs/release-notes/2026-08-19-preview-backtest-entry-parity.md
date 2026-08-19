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

**Superseded.** The warmup direction described above was reversed in
`2026-08-19-preview-backtest-bar-step-parity.md`. The backtest was the engine that was wrong: index
49 is the first bar with fifty bars of history, so the backtest had been one bar too strict. It was
loosened rather than the preview tightened, and `StrategyEvaluationPolicy.FirstEvaluableBarIndex`
now owns the boundary for both. The fallback-target correction above is unaffected.
