# Preferred LS regime benchmark correction

Backtests and optimization requests that omit `DataSource` now choose the market-regime benchmark
from the data source actually resolved from user settings.

Previously a user whose preferred source was LS Securities received Korean price data but the
regime builder still requested US `SPY` because the nullable request field was empty. That could
make the run fail or apply an unrelated fallback regime. The resolved LS source now uses `069500`;
Alpaca and Yahoo continue to use `SPY`. An integration regression test locks the preferred-source
selection and benchmark mapping.
