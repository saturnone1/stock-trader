# ML application boundary and status consistency

ML training now runs through an application-owned scoped use case instead of a singleton service
locator. Manual and scheduled requests still share one global execution claim, so a second training
request is rejected while the first is active.

Model status is captured as one immutable snapshot per model, preventing training-time responses
from mixing an old model's timestamp with a new model's metrics or feature importance. Status reads
use a dedicated singleton query and do not construct market-data or training-store adapters. The
`/api/ml` and `/api/ml/train` JSON property names are unchanged, but their success and error bodies
are now explicit OpenAPI contracts and generated TypeScript types.

No strategy, indicator, model-training formula, or historical trading result changes in this release.
