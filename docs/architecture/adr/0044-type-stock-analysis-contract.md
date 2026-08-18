# ADR 0044: Type the stock-analysis HTTP and desktop contract

## Status

Accepted

## Context

The stock-analysis endpoint returned an anonymous object. OpenAPI therefore exposed no reusable
response schema, while the Svelte recommendation page read Pascal-case members even though the
configured JSON serializer emits camel case. The page silently replaced every missing member with
zero, so valid prices, indicators, probabilities, and recommendation levels appeared blank or zero.

The same view also formatted both percentage-point values and fractional rates with one helper.
Historical win rate and signal confidence are fractions, so a value such as `0.625` was displayed
as `0.6%` rather than `62.5%`. Pattern cards displayed stable software codes instead of the central
investor-facing pattern name.

## Decision

- `StockAnalysisResponse` and its nested indicator and pattern contracts are the sole public HTTP
  shape for a stock analysis.
- The endpoint normalizes and validates symbols through `MarketSymbolPolicy`, maps the explicit
  response, and advertises its success and error schemas to OpenAPI.
- Pattern items retain their stable code and add the display name from `PatternCatalog`.
- The desktop API uses the generated `StockAnalysisResponse` type and consumes only canonical
  camel-case members. It does not retain Pascal-case or zero-valued compatibility fallbacks.
- Percentage-point metrics and 0-to-1 fractional rates use separate tested formatters.

## Consequences

Contract drift becomes visible during OpenAPI generation, TypeScript checking, Svelte build, and
architecture tests instead of being rendered as plausible zero values. Historical rate labels now
show their correct units, and users see the central Korean strategy name while integrations retain
the stable strategy code.
