# Provider-market intraday ingestion

- Split REST minute-bar ingestion from its background scheduler into a directly testable cycle and
  provider-bound data session.
- Corrected cross-market polling: Alpaca/Yahoo now poll only during the US regular session and LS
  Securities only during the Korean regular session.
- Made realtime replacement provider-specific. An active Alpaca stream no longer disables LS or
  Yahoo REST collection after the provider transition completes.
- Bound Alpaca streaming to the selected provider. A provider switch first rejects new Alpaca bar
  callbacks, disposes the connection, and flushes its buffered batch; REST collection waits during
  that handoff so two providers cannot write the same live series concurrently.
- Persist successful bars as one batch before notifying the live scanner, preserving the previous
  REST outcome while correcting streaming, which previously woke the scanner before persistence.
- Retain a failed streaming flush for serialized retry. The former implementation drained the
  bounded channel before saving and permanently lost that batch when SQLite failed.
- Ensure the flush loop is cancelled and drained when reconnect attempts are exhausted; previously
  that non-shutdown exit could wait forever on a periodic loop that had no completion signal.
- Surface a provider-wide latest-bar outage to configured retry and circuit-breaker handling instead
  of treating a cycle with zero successful requests as healthy.
- Replaced worker retry and cooldown constants with startup-validated typed settings and moved the
  periodic timer and delay to the injected clock.

This correctness fix can remove cross-market samples that were previously requested outside the
selected provider's regular session, and it prevents unrelated realtime state from creating gaps in
the selected provider's collection. There is no database-schema or public API change.
