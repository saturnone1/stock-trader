# ADR 0050: Make persistence and notification observation clocks explicit

## Status

Accepted

## Context

Several EF persistence shapes initialized audit fields with `DateTime.UtcNow`. Production use cases
already supplied their own creation, update, import, and discovery timestamps, but the entity defaults
could conceal a missing mapping and make a persistence adapter silently invent business history.
Discord alert/report payloads and an email alert footer also read the process clock directly. The email
footer used the server's unspecified local timezone, so identical events could render differently after
a host or container timezone change.

## Decision

- EF entities are passive persistence shapes and do not initialize audit fields from the system clock.
- Strategy, symbol-profile, financial-import, financial-snapshot, and optimization use cases continue to
  supply explicit timestamps across their purpose-specific persistence contracts.
- Discord and email notification adapters receive the registered `TimeProvider`.
- Generic notification observations are rendered as ISO UTC for Discord and explicitly labelled UTC in
  email. Trading-event timestamps already carried by recommendations remain event-owned.
- An architecture guard rejects direct system-clock access in these entities and adapters, and a payload
  characterization test proves that Discord uses the injected observation instant.

## Consequences

Missing timestamp ownership is visible as a default value during tests instead of being hidden by entity
construction. Replayed notification rendering is deterministic, and email output no longer depends on the
server's local timezone. There is no database schema or public HTTP contract change.
