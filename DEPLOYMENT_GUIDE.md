# StockTrader deployment

There are two supported execution paths. All older full-stack Dockerfiles, compose variants, and
single-process Blazor manifests have been removed.

## Local containers

Copy `.env.example` to `.env`, provide credentials, then run:

```bash
docker compose up --build
```

- Desktop: `http://localhost:8000`
- API health: `http://localhost:5239/api/health`
- The desktop calls `/api` on its own origin; nginx proxies it to the API container.

Stop the stack with `docker compose down`. Add `--volumes` only when intentionally deleting the
local SQLite volume.

## K3s production

Create the secret once without committing real values:

```bash
cp k8s/secret.example.yaml k8s/secret.yaml
# edit k8s/secret.yaml
sudo k3s kubectl apply -f k8s/secret.yaml
```

From a verified source snapshot on the K3s host, deploy both images and manifests with:

```bash
scripts/deploy-k3s.sh
```

An explicit immutable release tag may be supplied as the first argument. The script builds OCI
images, imports them into K3s, applies only the split API/Desktop manifests, waits for both
rollouts, and removes its temporary archives.

The API deployment uses `Recreate` because one SQLite database must never be opened by old and new
application Pods during a rollout. Verify the desktop URL, `/api/health`, Pod restart counts, and
API startup logs after deployment.
