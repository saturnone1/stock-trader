# Isolated live pattern scanning

- Split completed-daily-bar scanning from its background channel scheduler into a directly testable
  application cycle with focused data, regime, detection, and signal-processing boundaries.
- Moved retry, circuit-breaker threshold, and cooldown values into validated configuration and made
  retry/cooldown delays use the injected application clock.
- Corrected a reliability defect where a symbol was marked complete before detection and signal
  processing. A transient failure now remains eligible for retry on the same US market date.
- Preserved financial idempotency through the existing signal-bar, source-signal recommendation,
  and durable entry-claim identities.
- Added regression coverage for insufficient history, daily symbol deduplication, provider benchmark
  changes, shared regime caching and math, failed-processing retries, and persistence-before-order
  sequencing.
