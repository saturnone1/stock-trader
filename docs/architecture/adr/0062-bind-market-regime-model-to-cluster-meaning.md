# ADR 0062: Bind the market-regime model to causal features and cluster meaning

## Status

Accepted

## Context

The K-Means model, its feature calculations, cluster-label heuristics, and file persistence lived in
one classifier. The model ZIP and a separate `regime_labels.json` were written independently and had
no schema version or content binding. A missing or malformed label file silently assigned investor
labels by cluster number even though K-Means cluster numbers have no stable financial meaning.

Classification also consumed the last array element without applying the common as-of boundary.
An unordered or future-dated bar could therefore alter the ML regime while the deterministic
200-day trend fallback correctly ignored it.

## Decision

- One versioned feature schema and ordered feature catalog define every K-Means input.
- One feature factory sorts bars and excludes observations after the explicit as-of instant for both
  training and prediction. A window containing a non-positive close is not executable evidence.
- The model has exactly four clusters because the executable investor vocabulary has exactly four
  meanings: bullish, bearish, sideways, and high volatility.
- Cluster meaning is assigned from training evidence as a one-to-one mapping. The highest-volatility
  cluster is high volatility; the highest and lowest remaining 20-day returns are bullish and
  bearish; the remaining cluster is sideways.
- A manifest binds feature schema, cluster count, training time, sample count, the complete cluster
  mapping, and the SHA-256 hash of the model ZIP.
- A legacy, partial, mismatched, or tampered artifact is not executed. The classifier falls back to
  the shared deterministic `MarketRegimeTrendPolicy` until a current model is trained.

## Consequences

Existing regime models require retraining. This is intentional: arbitrary cluster-number fallbacks
cannot be proven equivalent to their original learned meaning. Prediction and training now share
one causal feature implementation, and model status reports actual feature samples rather than raw
downloaded bars. The classifier becomes a small coordinator while calculation, fitting, semantic
assignment, and storage can be tested independently.
