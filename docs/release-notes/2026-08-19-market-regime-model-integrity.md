# Market regime model integrity correction

The optional K-Means market-regime classifier previously loaded a model even when its separately
stored cluster-label file was missing or invalid. It then guessed meanings from cluster numbers,
although those numbers are arbitrary. Prediction could also use a future-dated or unordered final
bar that the deterministic long-trend policy excluded.

The classifier now:

- uses one completed-bar, as-of feature factory for training and prediction;
- assigns bullish, bearish, sideways, and high-volatility meanings exactly once from cluster
  evidence;
- loads only a model whose schema, complete label map, cluster count, and SHA-256 hash match its
  manifest; and
- falls back to the shared 200-day trend regime when no verified model is available.

Existing regime model files are intentionally invalidated and require retraining. This can change
historical ML-assisted live classifications because the former guessed label mapping and future-bar
behavior were not trustworthy. Deterministic preview/backtest behavior is unchanged.
