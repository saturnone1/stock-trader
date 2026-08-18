# ADR 0047: Isolate authentication state and security audit persistence

## Status

Accepted

## Context

The cookie authentication service directly queried and mutated `AppDbContext`, accepted EF user
entities as its working state, and read `DateTime.UtcNow` at several points in one login. Lockout
decisions were therefore difficult to replay at exact boundaries, while password policy, persistence,
claims construction, and audit side effects were coupled to one adapter. The audit service likewise
constructed EF entities and read the system clock while also resolving an HTTP client address.

Authentication does not change trading semantics, but it protects every live-trading API. Its
failure and time-boundary behavior therefore needs the same deterministic and storage-independent
application boundary as other operational use cases.

## Decision

- `AuthenticationService` owns PBKDF2 verification, registration validation, lockout transitions,
  password changes, and claims construction in `Application/Authentication`.
- `IAuthenticationUserStore` exposes only the user lookups and state transitions needed by that use
  case. Failed-login increments, lock creation, and successful reset are conditional atomic updates,
  so concurrent stale requests cannot lose an attempt or clear a newly created lock.
  `AuthenticationUserStore` is the sole mapper between those contracts and `AppUser`/EF Core.
- The service samples the injected `TimeProvider`; persisted creation, login, and lockout timestamps
  never come from entity initializers or the system clock.
- `ISecurityAuditSink` remains best effort so an audit storage outage cannot alter a login or logout
  result. The HTTP-aware adapter resolves the client address and observation time, then appends a
  storage-independent `SecurityAuditEntry` through `ISecurityAuditStore`.
- Security operational values remain under the existing `Security` configuration section, but are
  startup-validated and projected once into an immutable `AuthenticationPolicy`. Missing numeric
  values no longer acquire a second set of code defaults. Bootstrap visibility and registration
  enforcement consume that same policy snapshot.
- Existing endpoint paths, response messages, cookie claims, password format, and database schema
  remain unchanged.

## Consequences

Lockout expiry and state reset can be verified with exact clocks without a database. SQLite mapping
and audit failure isolation have focused adapter tests, and an architecture guard prevents the
application service from regaining EF, entity, or system-time dependencies. The existing case-
insensitive lookup behavior is preserved; a future normalized-username schema migration can now be
implemented entirely behind the user-store port.
