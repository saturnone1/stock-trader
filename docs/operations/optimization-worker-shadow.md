# Optimization Worker operations

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

## Verified remote-compute conformance

- Date: 2026-08-23
- API source/image: `5d8d79d` / `localhost/stock-trader/api:architecture-5d8d79d`
- Worker source/image: `62ed797` / `localhost/stock-trader/optimization-worker:architecture-62ed797`
- TLS generation: `20260823114459`
- Conformance job: `3` (`msa-shadow-conformance-5d8d79d`)

The supported deployment script built, imported, and independently rolled out the API and Worker.
Both Pods reached `1/1 Ready` with zero restarts. The Worker reported contract version 2,
`controlConnected=true`, an empty `controlError`, and shadow mode. A certificate-less request to the
internal HTTPS endpoint returned HTTP 401.

Job 3 evaluated four TQQQ daily-bar parameter combinations over 2025-01-01 through 2025-06-30 and
completed all four. The in-process authoritative run and the isolated Worker produced the same
ordered candidates, periods, trade counts, and financial metrics. Their normalized result hashes
were identical. `/api/health` then reported `awaiting=0`, `matches=1`, and `mismatches=0`; the API log
recorded `Optimization shadow comparison matched for Job 3`.

The integrated rollout exposed and corrected three deployment/contract defects before this match:
the API Service lacked a valid named multi-port mapping, the private client CA was rejected before
endpoint validation, and the Worker did not share the API's string-enum JSON convention. A first
semantically equal result also exposed decimal scale (`10.0` versus `10`) as an invalid source of
hash inequality. The comparison identity now normalizes decimal scale, guarded by a characterization
test. Failed validation jobs 1 and 2 were removed through the authenticated application API after
the corrected conformance job passed.

## Verified Remote authority cutover

- Date: 2026-08-23
- Source/images: `b636cb7`
- Runtime mode: `Remote`
- Worker replicas/concurrency: 2 / 2
- Active TLS generation after the drill: `otls-b636cb7`

This was one final service-level verification batch after the entire Optimization Worker authority
boundary was implemented. Local verification passed the backend build and all 1,000 tests, Worker
build, two compute tests, EF pending-model check, generated API check, 75 desktop tests, desktop
build, and Linux syntax checks for both operational scripts.

Remote jobs 4 and 5 ran simultaneously and were claimed by different Worker Pods. Each evaluated
the same four TQQQ daily candidates and produced the same ranks, periods, trade counts, financial
metrics, and normalized canonical hash as the prior in-process job 3. Job 7 was cancelled during a
10,000-candidate lease: both job and lease became cancelled, Worker cancellation telemetry advanced,
and no canonical result was committed.

For job 8, the generation-1 owner Pod was deleted while 10,000 candidates were running. The other
Pod reclaimed the expired lease as generation 2 and committed all results once. Job 9 survived an
API Pod deletion while its generation-1 lease remained active, then committed all 10,000 results
once after the replacement API recovered. Coordinator retry characterization also proved that an
already accepted result cannot create a second canonical result set.

The application and Worker were then deployed together in `Shadow`. Job 10 completed four
candidates through the in-process authority, created only a comparison lease, and matched the Worker
result. The official deployment script returned both workloads to `Remote`.

The TLS rotation script created generation `otls-b636cb7`. API and Worker jointly rolled to it and
reconnected successfully. They then jointly rolled back to preserved generation `20260823114459`,
remained healthy in `Remote`, and finally returned to `otls-b636cb7`. Prior generations remain
available for a controlled rollback.

The final API and two Worker Pods were Ready with zero restarts on `architecture-b636cb7`. Both
Workers reported ready `1`, control connected `1`, contract version `3`, and no active lease. Idle
samples were 39–41 millicores CPU and 33–35 MiB memory per Pod. Connection-refused warnings and
failure counters on one Worker corresponded to the intentional API downtime during joint rollout;
the retry loop recovered, final control connectivity was healthy, and the final API log had no
warning or error. Public `/api/health` returned `ok`, and authenticated strategy metadata reported
`Remote`, `usesRemoteWorker=true`, and two concurrent jobs.

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
it is missing. It is a second factor in addition to the generation-scoped mTLS workload identity.
The present single shared-secret generation does not provide zero-downtime secret rotation.
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

The Worker owns no database and cannot write canonical optimization or financial results. Strategy
Research owns durable lease/audit records, while the in-process optimizer remains authoritative in
shadow mode. Rollback does not delete those records or require financial reconciliation. Stop the
workload immediately with:

```bash
sudo k3s kubectl -n stocktrader scale \
  deployment/stocktrader-optimization-worker --replicas=0
```

Restore the previous image with `kubectl rollout undo` only when the previous ReplicaSet references
an image still present in the local K3s container store. Otherwise check out the desired source
commit and redeploy only the `optimization-worker` scope. Confirm the in-process optimizer remains
enabled before any future remote-compute rollback; that condition is automatic in the current
shadow release.

## mTLS certificate rotation

Generate separate API-server and Worker-client Secrets from one short-lived internal CA without
printing private material:

```bash
./scripts/rotate-optimization-worker-tls.sh
```

The default leaf validity is 90 days and can be changed to 7–397 days with
`STOCKTRADER_WORKER_TLS_DAYS`. Each rotation creates a generation-named pair of Secrets and updates
the active-generation ConfigMap without deleting prior generations. Redeploy API and Worker together
through `scripts/deploy-k3s.sh` after rotation. Verify the API still answers `/api/health` on port 5239, the Worker reports an empty
`controlError`, and a plaintext or certificate-less request to the internal Worker endpoint is
rejected. For certificate rollback, set `STOCKTRADER_WORKER_TLS_GENERATION` to the previous preserved
generation and redeploy API and Worker together. Delete old certificate Secrets only after the new
generation passes the rotation and rollback drills.

## Current limitations

- This is one physical K3s node and is not high availability.
- Remote mode intentionally rejects wall-clock duration limits because stopping on elapsed time is
  not deterministic across Pods. Deterministic tested-combination limits remain supported.
- Prometheus-format metrics exist, but no cluster scraper, retention, dashboard, or alert has yet
  been selected.
- Resource evidence is from the exercised single-node environment and is not a general capacity
  model or an SLO.
