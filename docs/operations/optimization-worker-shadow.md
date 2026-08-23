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

## Verified control-plane handshake

- Date: 2026-08-23
- Source commit: `cd9b740`
- Image: `localhost/stock-trader/optimization-worker:architecture-cd9b740`
- API transport mode: authenticated status probe only

The API and Worker were rolled out independently. The replacement Worker Pod reached `1/1 Ready`
with zero restarts. After three 30-second probe intervals, `/health/ready` reported
`controlConfigured=true`, `controlConnected=true`, and an empty `controlError`. Metrics reported
three attempts and three successes. A request carrying an invalid secret returned HTTP 401, while
the same status request with the mounted credential returned HTTP 200.

Each probe attempt has a five-second deadline. This prevents an unreachable control service from
stalling the background loop and leaves the Worker ready in shadow mode while exposing the failure
type through `controlError` and warning logs.

## Control-plane secret

Create the independent authentication secret without writing it to the repository or shell output:

```bash
worker_secret="$(openssl rand -hex 32)"
sudo k3s kubectl -n stocktrader create secret generic \
  stocktrader-optimization-worker-auth \
  --from-literal=shared-secret="$worker_secret"
unset worker_secret
```

Both API and Worker manifests refer to this Secret. The deployment script fails before building if
it is missing. Shadow mode tolerates probe downtime, but the present single-secret generation does
not provide zero-downtime rotation and must not be treated as the final remote-compute identity.
Use `--from-literal` as shown: creating the value from standard input can preserve a trailing
newline, causing every otherwise valid request to be rejected with HTTP 401.

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

The currently deployed handshake release owns no durable lease. Releases containing ADR 0074 add
Strategy Research-owned audit records, but the K3s lease switch remains false and the Worker still
owns no database. Rollback does not delete those records or require financial reconciliation. Stop
the workload immediately with:

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
- Durable lease APIs exist behind `LeaseTransportEnabled=false`; no executable lease transport or
  remote computation is enabled in K3s.
- The authenticated status handshake uses node-local cluster HTTP; executable leases require the
  internal TLS/workload-identity gate.
- Prometheus-format metrics exist, but no cluster scraper, retention, dashboard, or alert has yet
  been selected.
- Idle resource evidence does not satisfy the Stage 2 load/chaos/cost gate.
