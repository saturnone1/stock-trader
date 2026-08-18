# Compiled custom-position instruction parity

Preview, backtest, and live execution now use one application resolver to turn compiled custom
close and scaling conditions into execution-session instructions. The shared resolver owns current
profit calculation, persisted scaling-count input, rule index/direction/percentage mapping, and the
custom close reason. Each environment still owns only its causal bar window and available portfolio
capital.

Fill priority, scaling quantity, capital limits, and backtest results are unchanged. Live
custom-rule exits now store the canonical `청산 규칙 충족` reason already used by preview and
backtest instead of a strategy-name-specific label. The former difference described the same
financial event with incompatible text and was therefore incorrect.
