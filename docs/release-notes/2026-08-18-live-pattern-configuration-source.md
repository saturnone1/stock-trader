# Live pattern configuration source and unavailable strategy correction

- Corrected opening-range breakout and earnings-drift strategies that were shown as runnable even
  though their detectors always returned no signal.
- Backtests now fail with an explicit reason instead of returning a misleading zero-trade result
  for those strategies.
- Settings keeps unavailable strategies visible with the missing-data reason. Settings and symbol
  profiles both prevent those strategies from being enabled.
- Live parameter promotion now validates its complete risk/position input and stores one database
  snapshot. It no longer rewrites `appsettings.json` inside the running container.
- Live entry detection now resolves the same persisted parameter overrides already used by live
  exit evaluation.
