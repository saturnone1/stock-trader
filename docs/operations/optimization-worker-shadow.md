# Optimization Worker shadow operations

## Verified rollout

- Date: 2026-08-23
- Source commit: `d1014e4`
- Image: `localhost/stock-trader/optimization-worker:architecture-d1014e4`
- Workload: `stocktrader/stocktrader-optimization-worker`
- Mode: `shadow` — no lease claim, heartbeat, result submission, database, or financial write

The image was built with Buildah, imported into K3s, and deployed through the only supported script:

```bash
STOCKTRADER_DEPLOY_SCOPE=optimization-worker ./scripts/deploy-k3s.sh d1014e4
```

The observed rollout was `1/1 Ready`, one Running Pod, and zero restarts. Both probes returned HTTP
200. `/health/ready` reported contract version 2 and shadow mode, and `/metrics` reported ready `1`.
The first idle sample was 1 millicore CPU and 20 MiB memory. This single sample is deployment
evidence, not a capacity baseline or an SLO.

The applied Pod reported the dedicated `stocktrader-optimization-worker` ServiceAccount, disabled
token automount, a read-only root filesystem, and the immutable source-tagged image. It has no data
volume and no application secret.

## Routine verification

```bash
sudo k3s kubectl -n stocktrader rollout status \
  deployment/stocktrader-optimization-worker --timeout=180s
sudo k3s kubectl -n stocktrader get pod -l app=stocktrader-optimization-worker
sudo k3s kubectl -n stocktrader logs deployment/stocktrader-optimization-worker --tail=100
sudo k3s kubectl -n stocktrader top pod -l app=stocktrader-optimization-worker
```

Use a temporary local port-forward to inspect health without exposing a cluster Service:

```bash
sudo k3s kubectl -n stocktrader port-forward \
  deployment/stocktrader-optimization-worker 18080:8080
curl --fail http://127.0.0.1:18080/health/ready
curl --fail http://127.0.0.1:18080/metrics
```

## Rollback

Shadow mode owns no durable state, so rollback does not require database backup, restore, or job
reconciliation. Stop the workload immediately with:

```bash
sudo k3s kubectl -n stocktrader scale \
  deployment/stocktrader-optimization-worker --replicas=0
```

Restore the previous image with `kubectl rollout undo` only when the previous ReplicaSet references
an image still present in the local K3s container store. Otherwise check out the desired source
commit and redeploy only the `optimization-worker` scope. Confirm the in-process optimizer remains
enabled before any future remote-compute rollback; that condition is automatic in the current
shadow release.

## Current limitations

- This is one physical K3s node and is not high availability.
- No authenticated lease transport or remote computation is enabled.
- Prometheus-format metrics exist, but no cluster scraper, retention, dashboard, or alert has yet
  been selected.
- Idle resource evidence does not satisfy the Stage 2 load/chaos/cost gate.
