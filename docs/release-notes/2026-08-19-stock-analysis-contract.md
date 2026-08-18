# Stock analysis contract and percentage correction

- Recommendation detail now consumes an explicit generated API contract, fixing valid analysis
  values that previously appeared as zero or blank because the page read the wrong JSON casing.
- Current price, stop, target, ATR, indicators, probability, expected return, risk, confidence, and
  analysis time now render from their canonical camel-case fields.
- Active-pattern confidence and historical win rate now convert their stored 0-to-1 fractions to
  percentages. For example, `0.625` is displayed as `62.5%`, not `0.6%`.
- Active patterns display the central investor-facing strategy name while preserving their stable
  strategy code in the API.
- Analysis symbols now use the shared market-symbol normalization and validation policy.
