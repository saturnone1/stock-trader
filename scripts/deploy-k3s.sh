#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

release_tag="${1:-$(git rev-parse --short=12 HEAD)}"
deploy_scope="${STOCKTRADER_DEPLOY_SCOPE:-all}"
api_image="localhost/stock-trader/api:architecture-${release_tag}"
desktop_image="localhost/stock-trader/desktop:architecture-${release_tag}"
worker_image="localhost/stock-trader/optimization-worker:architecture-${release_tag}"
archive_dir="$(mktemp -d /tmp/stocktrader-deploy.XXXXXX)"
data_dir=""
stocktrader_host=""
migration_container="stocktrader-migrate-${release_tag}"

deploy_api=false
deploy_desktop=false
deploy_worker=false
case "$deploy_scope" in
  all) deploy_api=true; deploy_desktop=true; deploy_worker=true ;;
  api) deploy_api=true ;;
  desktop) deploy_desktop=true ;;
  optimization-worker) deploy_worker=true ;;
  *)
    echo "STOCKTRADER_DEPLOY_SCOPE must be all, api, desktop, or optimization-worker." >&2
    exit 1
    ;;
esac

if $deploy_api; then
  data_dir="${STOCKTRADER_DATA_DIR:?Set STOCKTRADER_DATA_DIR to the absolute host data directory}"
fi
if $deploy_desktop; then
  stocktrader_host="${STOCKTRADER_HOST:?Set STOCKTRADER_HOST to the public hostname}"
fi

if $deploy_api && { [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; }; then
  echo "STOCKTRADER_DATA_DIR must be a safe absolute path below the filesystem root." >&2
  exit 1
fi

if $deploy_desktop && { [[ ! "$stocktrader_host" =~ ^[A-Za-z0-9.-]+$ ]] || [[ "$stocktrader_host" != *.* ]]; }; then
  echo "STOCKTRADER_HOST must be a valid DNS hostname." >&2
  exit 1
fi

cleanup() {
  sudo buildah rm "$migration_container" >/dev/null 2>&1 || true
  sudo rm -f -- "$archive_dir/api.tar" "$archive_dir/desktop.tar" "$archive_dir/worker.tar"
  rmdir "$archive_dir" 2>/dev/null || true
}
trap cleanup EXIT

sudo k3s kubectl apply -f k8s/namespace.yaml
if $deploy_api && ! sudo k3s kubectl -n stocktrader get secret stocktrader-alpaca >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-alpaca." >&2
  echo "Create it from k8s/secret.example.yaml before deploying." >&2
  exit 1
fi

if $deploy_api; then
  sudo buildah bud --layers -f Dockerfile.api -t "$api_image" .
  sudo buildah push "$api_image" "oci-archive:$archive_dir/api.tar:$api_image"
fi

if $deploy_desktop; then
  sudo buildah bud --layers -f Dockerfile.desktop -t "$desktop_image" .
  sudo buildah push "$desktop_image" "oci-archive:$archive_dir/desktop.tar:$desktop_image"
fi

if $deploy_worker; then
  sudo buildah bud --layers -f Dockerfile.optimization-worker -t "$worker_image" .
  sudo buildah push "$worker_image" "oci-archive:$archive_dir/worker.tar:$worker_image"
fi

if $deploy_api && sudo k3s kubectl -n stocktrader get deployment stocktrader-api >/dev/null 2>&1; then
  # A RollingUpdate deployment contains a server-defaulted rollingUpdate field.
  # Clear it atomically while switching strategy so Kubernetes accepts Recreate.
  sudo k3s kubectl -n stocktrader patch deployment stocktrader-api --type=merge \
    -p '{"spec":{"strategy":{"type":"Recreate","rollingUpdate":null}}}'
  sudo k3s kubectl -n stocktrader scale deployment stocktrader-api --replicas=0
  if sudo k3s kubectl -n stocktrader get pod -l app=stocktrader-api --no-headers | grep -q .; then
    sudo k3s kubectl -n stocktrader wait --for=delete pod -l app=stocktrader-api --timeout=180s
  fi
fi

if $deploy_api; then
  sudo install -d -m 0750 "$data_dir/backups"
  if sudo test -f "$data_dir/stocktrader.db"; then
    if ! command -v sqlite3 >/dev/null 2>&1; then
      echo "sqlite3 is required to create a consistent pre-migration backup." >&2
      exit 1
    fi
    backup_path="$data_dir/backups/stocktrader-pre-${release_tag}-$(date -u +%Y%m%dT%H%M%SZ).db"
    sudo sqlite3 "$data_dir/stocktrader.db" ".backup '$backup_path'"
    sudo sqlite3 "$backup_path" "PRAGMA quick_check;" | grep -qx ok
    echo "Database backup: $backup_path"
  fi

  sudo buildah from --name "$migration_container" \
    --volume "$data_dir:/data:rw" "$api_image" >/dev/null
  sudo buildah run "$migration_container" -- dotnet StockTrader.dll --migrate-database
  sudo buildah rm "$migration_container" >/dev/null
fi

# Import immediately before the manifests reference these tags. Otherwise K3s image
# garbage collection can remove the still-unreferenced images during backup/migration.
if $deploy_api; then
  sudo k3s ctr images import "$archive_dir/api.tar"
fi
if $deploy_desktop; then
  sudo k3s ctr images import "$archive_dir/desktop.tar"
fi
if $deploy_worker; then
  sudo k3s ctr images import "$archive_dir/worker.tar"
fi

if $deploy_api; then
  sed -e "s|localhost/stock-trader/api:latest|$api_image|" \
    -e "s|__STOCKTRADER_DATA_DIR__|$data_dir|" k8s/deployment-api.yaml \
    | sudo k3s kubectl apply -f -
fi
if $deploy_desktop; then
  sed -e "s|localhost/stock-trader/desktop:latest|$desktop_image|" \
    -e "s|__STOCKTRADER_HOST__|$stocktrader_host|" k8s/deployment-desktop.yaml \
    | sudo k3s kubectl apply -f -
fi
if $deploy_worker; then
  sed -e "s|localhost/stock-trader/optimization-worker:latest|$worker_image|" \
    k8s/deployment-optimization-worker.yaml \
    | sudo k3s kubectl apply -f -
fi

if $deploy_api; then
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-api --timeout=300s
fi
if $deploy_desktop; then
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-desktop --timeout=180s
fi
if $deploy_worker; then
  sudo k3s kubectl -n stocktrader rollout status \
    deployment/stocktrader-optimization-worker --timeout=180s
fi

selected_apps=()
$deploy_api && selected_apps+=(stocktrader-api)
$deploy_desktop && selected_apps+=(stocktrader-desktop)
$deploy_worker && selected_apps+=(stocktrader-optimization-worker)
sudo k3s kubectl -n stocktrader get deployment "${selected_apps[@]}"
selector="$(IFS=,; echo "${selected_apps[*]}")"
sudo k3s kubectl -n stocktrader get pods -l "app in (${selector})"
