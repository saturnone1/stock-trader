# Causal signal-scoring evidence

- Signal-scoring features are now captured from completed bars at the original signal time and
  persisted with a versioned schema.
- Broker-confirmed positions and realized trades retain the source signal, so partial exits are
  grouped into one original entry outcome for training.
- Training now uses a chronological future holdout and rejects datasets whose train or validation
  partition lacks both wins and losses.
- Planned stop/target R:R replaces the former exit-price-derived value, removing result leakage.
- Displayed feature importance is measured by permutation instead of fixed guessed percentages.
- Model manifests verify the feature schema and model SHA-256 before an artifact can affect live
  confidence. Container model storage now uses the persistent `/data` volume.

Existing signal-scoring model files have no compatible causal manifest and will not be loaded.
Signal confidence therefore falls back to the deterministic strategy value until at least the
configured minimum number of new, completed causal samples is available. Historical rows are kept
for audit but are not backfilled with invented features.
