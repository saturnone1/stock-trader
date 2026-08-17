# Pattern preview end-boundary correction

Pattern preview now treats the requested `To` timestamp as an exclusive simulation boundary,
matching the bars displayed on the chart. The SQLite repository intentionally returns bars through
the supplied boundary (`Timestamp <= to`). Previously the preview hid a bar exactly at `To` but
still evaluated entries and exits on it, so an invisible next-day or end-minute stop/target could
change the reported return and open-position state.

This is an intentional correction to historical preview results. Backtests are unchanged. The
`RunAsync_DoesNotEvaluateTheRepositoryInclusiveEndBar` characterization fixture locks the corrected
behavior, and the NextOpen golden fixture continues to lock entry repricing and entry-bar execution.
