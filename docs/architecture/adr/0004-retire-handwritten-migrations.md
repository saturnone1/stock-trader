# ADR 0004: Retire handwritten startup migrations

Status: Accepted

Date: 2026-08-18

## Context

ADR 0003 introduced a temporary bridge for SQLite databases created before EF Core migration
history existed. That bridge could create tables, add columns, repair data, and then record the EF
initial baseline. It was intentionally limited to three frozen compatibility migrations.

The production database has now adopted `20260817204350_InitialSchema`. A pre-adoption backup is
retained, startup and `/api/health` repeatedly report the same applied/latest migration with zero
pending migrations, and row counts and SQLite integrity were verified during adoption. Keeping the
writer after this evidence would leave a second schema mutation engine in every future release.

## Decision

EF Core is the only startup schema writer.

- `DatabaseSchemaMigrator` accepts an empty database or a database with EF migration history and
  delegates all changes to `Database.MigrateAsync`.
- A database that contains application tables but no applied EF migration fails closed before any
  schema or row change. Its error directs the operator to an EF-baselining historical release or a
  verified backup.
- `IDatabaseMigration`, its runner and SQLite mutation context, all three handwritten migrations,
  the compatibility validator, and the baseline-writing CLI mode are removed.
- `--verify-database-migrations` is a read-only preflight that succeeds only when the applied and
  latest EF migrations match and no migration is pending.
- The old `__StockTraderMigrations` table may remain as inert historical data in an adopted
  database. Current code neither reads nor writes it.
- New schema changes must be represented by generated and reviewed files in `Data/EfMigrations`.

## Consequences

- Startup has one schema owner and cannot silently repair or reinterpret an unknown legacy shape.
- Old unbaselined database copies require an explicit operator-controlled upgrade using repository
  history; this is safer than mutating them under the current model.
- Architecture tests fail if another handwritten migration pipeline or startup DDL is introduced.
- The retained production backup remains the recovery boundary for the one-time baseline adoption.
