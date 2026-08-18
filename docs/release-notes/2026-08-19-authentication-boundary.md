# Authentication boundary and deterministic lockout timing

- Moved login, registration, password change, PBKDF2 verification, and lockout policy behind a
  storage-independent application service.
- Replaced direct authentication/audit EF access with purpose-specific SQLite stores.
- Login, lockout, registration, and audit timestamps now use the injected application clock.
- Failed-login and successful-login state changes are conditional atomic updates; overlapping stale
  requests can no longer undercount failures or clear a newer account lock.
- Added exact lockout-boundary, malformed-credential, password-change, case-insensitive lookup, and
  best-effort audit regression tests.
- Existing HTTP routes, cookies, user rows, password hashes, and operator-facing messages remain
  compatible; historical trading results are unaffected.
