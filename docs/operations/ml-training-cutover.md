# ML Training service cutover

This runbook is the single operational record for ADR 0079. Do not begin another extraction while
any acceptance item below is open.

## Ownership

- The API owns completed Market Data evidence, causal feature preparation, signal/outcome reads,
  scheduling, and the read-only inference cache.
- `stocktrader-ml-training` owns `jobs.db`, publication revision, immutable model artifacts, retries,
  cancellation, and training computation orchestration.
- `StockTrader.MlTrainingCompute` is the only owner of ML.NET pipelines. Local, Shadow, and Remote
  use the same facade.
- The service mounts neither `stocktrader.db`, `marketdata.db`, nor `/data/ml_models`. It receives no
  broker or data-provider credentials.
- Signal training legitimately publishes no artifact until enough causal, chronologically
  splittable win/loss samples exist. Never seed production with fabricated labels.

## Configuration

Create a random `stocktrader-ml-training-auth` secret from the example, then create a generation
scoped private CA and both workload certificates:

```bash
scripts/rotate-ml-training-tls.sh
```

Deploy only this complete boundary with the supported script:

```bash
export STOCKTRADER_DEPLOY_SCOPE=ml-training
export STOCKTRADER_ML_TRAINING_DIR=/home/saturnone1/stock-trader-ml-training-data
scripts/deploy-k3s.sh
```

The API and service must then be deployed together for `Shadow` and later `Remote`:

```bash
export STOCKTRADER_DEPLOY_SCOPE=all
export STOCKTRADER_ML_TRAINING_MODE=Shadow # then Remote after parity evidence
scripts/deploy-k3s.sh
```

`Remote` never silently executes Local training. Existing verified inference cache or deterministic
non-ML fallback remains available when the training service is unavailable.

## Verification record

Completed service-unit and production batch on 2026-08-24 (`bb23084`):

- API build and independent F# service build: passed.
- Contract mutation/future sample/incomplete label rejection: passed.
- Same compute facade, deterministic prediction parity, explicit insufficient-signal result: passed.
- SQLite job identity idempotency/conflict/cancellation: passed.
- Existing application suite: 1,007 passed; independent ML service: 4 passed repeatedly.
- Desktop API check, 75 tests, and production build: passed.
- Production authority: `Remote`, publication revision 6, TLS generation `ml240019`.

Cluster evidence to record before ADR acceptance:

- [x] image build/import and independent Pod, ServiceAccount, NetworkPolicy, two-port probes
- [x] mTLS CA validation, client identity rejection, shared-secret rejection and TLS rotation
- [x] production Shadow parity for regime and explicit insufficient signal
- [x] Remote-only training, verified API cache promotion, API restart reload
- [x] duplicate/concurrent/replay, cancellation, unavailable/no-fallback and corrupt/stale rejection
- [x] Pod loss while pending/running and API loss during completion
- [x] bounded CPU/memory load and queue behavior
- [x] SQLite backup, restore, immutable artifact rehydration and rollback to Local
- [x] `/api/health`, `/api/ml`, desktop URL, Pod status and startup logs

Operational observations: 12 simultaneous duplicate submissions all returned 202 without creating
another job; the service remained at 11m CPU and 49 MiB after the batch (2 CPU/1 GiB limits).
Removing the latest immutable ZIP/manifest and restarting reproduced byte-identical SHA-256 files
from `jobs.db`. The restore script preserved revision 5, and the final Remote publication advanced
to revision 6. API and desktop health both returned HTTP 200 with every StockTrader Pod Ready.

## Backup and restore

Every deploy uses SQLite online backup and verifies `PRAGMA quick_check`. The job result JSON stores
the exact validated model bytes; startup rehydrates any missing immutable file from completed jobs,
so the SQLite backup is the recoverable publication source. Restore only from the service backup
directory:

```bash
STOCKTRADER_ML_TRAINING_DIR=/home/saturnone1/stock-trader-ml-training-data \
  scripts/restore-ml-training-backup.sh /home/saturnone1/stock-trader-ml-training-data/backups/<file>.db
```

Rollback sets `STOCKTRADER_ML_TRAINING_MODE=Local`, backs up `/data/ml_models`, and redeploys the API.
Never mount the service directory into the API or copy its database into the application database.
