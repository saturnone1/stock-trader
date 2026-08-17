# ADR 0007: Generate desktop API contracts from build-time OpenAPI

## Status

Accepted

## Context

The Svelte desktop copied backend request and response shapes into `src/api/types.ts`. A server
field could therefore be renamed, removed, or retyped while the desktop continued to compile
against a stale handwritten interface. Runtime-only OpenAPI would also require starting the
production application, exposing a documentation endpoint, and risking database or hosted-worker
side effects during contract generation.

## Decision

ASP.NET Core generates the `desktop` OpenAPI document during the backend build. The generated JSON
is committed under `desktop-app/openapi`, and `openapi-typescript` deterministically produces
`desktop-app/src/api/generated.ts`. Desktop-facing aliases reference the generated component schemas
instead of repeating their fields.

Build-time generation is detected through the `GetDocument.Insider` entry assembly. In that mode the
host does not read user secrets, initialize or migrate the database, or start hosted background
loops. Services used as minimal-API parameters remain registered so request metadata is inferred
correctly. The OpenAPI HTTP endpoint is mapped only in the Development environment.

`npm run api:generate` rebuilds the document and generated TypeScript. `npm run api:check` performs
the same backend generation and fails when the committed TypeScript is stale. CI additionally
requires both generated files to remain unchanged after regeneration.

## Consequences

- Backend contract changes become desktop compile-time changes instead of delayed runtime failures.
- Endpoint response metadata must be explicit where a handler returns the general `IResult` type.
- Contract generation remains side-effect free and does not broaden the production attack surface.
- The generated file covers the whole API, while handwritten aliases can be retired feature by
  feature without forcing one large frontend rewrite.
