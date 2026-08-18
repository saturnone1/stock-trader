# ADR 0063: Make ML training an application use case

## Status

Accepted

## Context

The ML training coordinator was registered as a singleton and opened nested dependency-injection
scopes to locate the selected data feed and signal-training store. API and background adapters
depended on interfaces and status types owned by `Services/ML`, while the API returned anonymous
objects that produced no usable OpenAPI response schemas.

Changing the coordinator to a normal scoped use case without another decision would remove the
singleton's process-wide concurrent-training guard. Separate API and background scopes could then
train and replace the same model files simultaneously. Model status was also assembled by reading
several mutable properties independently, allowing fields from two model generations to be mixed.

## Decision

- Classifier, scorer, training use-case, result, status, and feature-importance contracts belong to
  `Application/MachineLearning` and do not depend on Services, persistence, EF, or ASP.NET.
- `MLModelTrainingService` is a scoped implementation with constructor-injected data-feed and causal
  training-store dependencies. It does not create scopes or locate services.
- One singleton `MlTrainingRunState` owns the process-wide training claim and operator-visible run
  status across every scoped API or background use case.
- Each model exposes one immutable status snapshot captured under the same lock that publishes a
  newly trained model. A singleton, read-only `IMlModelStatusQuery` combines those snapshots without
  constructing scoped market-data or training-store adapters.
- `MlEndpoints` depends only on purpose-specific application training and status use cases and maps
  named response contracts. The generated OpenAPI and TypeScript contracts describe success and
  failure bodies explicitly while retaining the established JSON property names.

## Consequences

API and worker adapters no longer know ML implementation classes or assemble model state. Scoped
dependencies follow normal lifetime validation, concurrent manual and scheduled retraining remains
rejected process-wide, and status responses cannot combine fields from different model generations.
This changes no indicator, training, prediction, or trading formula.
