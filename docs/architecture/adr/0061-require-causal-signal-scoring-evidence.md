# ADR 0061: Require causal signal-scoring evidence

## Status

Accepted

## Context

The live signal scorer predicted with RSI, Bollinger position, volume, market regime, ATR,
historical win rate, planned R:R, and long-trend distance. Its trainer received only completed
`TradeRecord` rows and substituted neutral constants for most of those fields. It also calculated
the training R:R feature from the eventual exit price, which leaked the result into an input that
was presented as entry-time evidence. Random train/test splitting allowed later outcomes to inform
the model evaluated against earlier rows. Displayed feature importance values were fixed guesses,
not measurements from the trained model.

Persisted model files had no feature-schema or content identity. A deployment could therefore load
an artifact trained under incompatible semantics, and the relative container path placed artifacts
outside the persistent data volume.

## Decision

- `SignalScoringFeatureSchema` versions the exact feature vector. A common feature factory uses the
  shared indicator service and ignores bars later than `PatternSignal.SignalBarAt`.
- Every detected signal records its RSI, Bollinger position, volume ratio, market-regime code, ATR
  fraction, historical win rate, planned stop/target R:R, long-moving-average distance, and explicit
  long-history availability before persistence.
- `SourceSignalId` travels from recommendation to broker-confirmed position and every realized trade.
  The training store groups all partial exits by that identity and labels the original decision from
  total realized PnL. Legacy signals, positions, and trades without complete causal evidence remain
  valid audit data but are excluded from training.
- The scorer accepts typed `SignalScoringTrainingSample` values instead of persistence entities. It
  trains on the oldest 80 percent and validates on the newest 20 percent. Both partitions must
  contain wins and losses.
- Feature importance is measured by deterministic permutation on the future validation partition.
  Hardcoded importance weights are removed.
- Each model is accompanied by a manifest containing the feature version/count, training instant,
  sample count, validation metrics, feature importances, and SHA-256 of the model. Missing,
  incompatible, or mismatched manifests make the artifact non-executable.
- Docker Compose and K3s place `ML.ModelDirectory` under the existing `/data` persistent volume.

## Consequences

Previously trained signal-scoring artifacts intentionally stop affecting confidence because their
inputs cannot prove causal parity. Until enough newly captured signals have completed outcomes and
both chronological partitions contain wins and losses, live detection uses the deterministic
strategy confidence unchanged. This fail-closed interval is preferable to applying a model with
target leakage or incompatible feature semantics.

The schema migration only adds nullable evidence columns and indexes. It does not invent features
for historical rows. Partial exits no longer overweight one entry decision during training, and
reported accuracy, AUC, and feature importance now describe a forward chronological holdout.
