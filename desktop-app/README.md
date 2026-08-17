# StockTrader desktop

The Svelte/Vite application is the only operational StockTrader user interface. It calls the JSON
API through same-origin `/api` routes in production.

## Commands

```text
npm ci
npm run dev
npm run test
npm run build
```

When a backend request or response contract changes, regenerate the committed OpenAPI and
TypeScript artifacts from this directory:

```text
npm run api:generate
```

`api:generate` builds the backend in side-effect-free contract mode, writes
`openapi/stocktrader_desktop.json`, and generates `src/api/generated.ts`. Do not edit either file by
hand. `npm run api:check` fails if the generated TypeScript is stale, and CI also verifies that a
fresh backend generation does not change either committed artifact.

Feature-facing aliases remain in `src/api/types.ts`; shared server contracts must reference
`components['schemas'][...]` from the generated file instead of copying backend fields.
