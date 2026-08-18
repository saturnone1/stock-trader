# ADR 0059: Centralize market-regime trend evidence

## Status

Accepted

## Context

Pattern preview, backtest preparation, live scanning, stock analysis, and the ML classifier each
calculated the benchmark's 200-day trend separately. Their insufficient-history behavior was not
equivalent. Preview and the backtest fallback assumed a bull market, live scanning returned an
unknown bearish boolean, and the ML fallback labelled short history as bearish. Preview also used
`>=` while the other execution paths used strict `>`, so a close exactly on the average could pass
only in preview. The preview fallback could expose `SpyAbove200Ma = true` with a bearish label.

These differences violated the invariant that preview, backtest, and live execution receive the
same compiled-strategy semantics. Optimistic fallback data was especially unsafe because it could
enable bull-only entries without the required benchmark evidence.

## Decision

- `MarketRegimeTrendPolicy` is the single deterministic owner of the completed-bar 200-day trend.
- The policy filters out observations after the explicit `asOf` boundary, orders the remaining
  evidence, and evaluates exactly the trailing central-catalog window.
- A bull regime requires a positive moving average and a latest close strictly above it. Equality
  is bearish.
- Fewer than 200 completed benchmark bars produce an explicit unknown label and a false bullish
  flag. Execution therefore fails closed instead of inventing favorable history.
- Preview, backtest map preparation and lookup, live scanning, stock analysis, and the ML base
  regime delegate to the policy. The old analysis-only long-trend calculation and duplicate
  `MinimumRegimeBars` setting are removed.

## Consequences

The same benchmark bars and cutoff now produce the same trend state in every execution mode, and
future bars cannot influence a historical preview or backtest classification. Backtests or previews
that previously relied on fewer than 200 benchmark bars may now show fewer bull-only entries or use
bear-market portfolio weights. This is an intentional historical-result correction: the previous
result was based on an unsupported optimistic assumption rather than market evidence.
