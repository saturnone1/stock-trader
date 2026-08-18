# ADR 0024: Isolate settings management

## Status

Accepted

## Context

The settings endpoint bound the EF `UserSettings` entity directly, merged fields in the HTTP
handler, and read system time in both the endpoint and repository. Its anonymous response exposed
masked secret prefixes and produced no OpenAPI schema. The desktop expected PascalCase properties,
invented unsupported fallback codes, accepted provider and order mode as free text, and treated a
symbol list as a text scalar without an explicit conversion.

These defects made a safety-critical operating configuration hard to validate and easy for the API,
persistence model, and UI to interpret differently.

## Decision

- `SettingsManagementService` owns validation, normalization, merge semantics, and modification time.
- `ISettingsManagementStore` carries a storage-independent snapshot; the SQLite adapter alone maps
  the EF entity.
- GET and PUT use explicit API contracts that generate OpenAPI and TypeScript schemas.
- Secret values are accepted only on writes. Reads expose configured-state booleans and never a
  secret prefix or masked value.
- `OrderMode` moves to `Domain.Trading` without changing its names or persisted integer values, and
  `OrderModeCatalog` owns operator-facing labels and explanations.
- The settings response projects implemented providers and built-in strategies from their domain
  catalogs. The desktop renders those choices and uses a pure, tested model to normalize watchlists
  and numeric request values.

## Consequences

Invalid risk limits, unsupported providers, custom-strategy enum leakage, malformed symbols, and
unknown order modes fail before persistence. API and UI cannot drift to fictional fallback values,
and an API read cannot disclose reusable notification credentials. Existing SQLite values and JSON
enum codes remain compatible.
