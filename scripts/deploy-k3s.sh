#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

release_tag="${1:-$(git rev-parse --short=12 HEAD)}"
deploy_scope="${STOCKTRADER_DEPLOY_SCOPE:-all}"
api_image="localhost/stock-trader/api:architecture-${release_tag}"
desktop_image="localhost/stock-trader/desktop:architecture-${release_tag}"
worker_image="localhost/stock-trader/optimization-worker:architecture-${release_tag}"
market_data_image="localhost/stock-trader/market-data:architecture-${release_tag}"
ml_training_image="localhost/stock-trader/ml-training:architecture-${release_tag}"
trading_core_image="localhost/stock-trader/trading-core:architecture-${release_tag}"
archive_dir="$(mktemp -d /tmp/stocktrader-deploy.XXXXXX)"
data_dir=""
stocktrader_host=""
migration_container="stocktrader-migrate-${release_tag}"
tls_generation="${STOCKTRADER_WORKER_TLS_GENERATION:-}"
server_tls_secret=""
client_tls_secret=""
lease_transport_enabled="${STOCKTRADER_OPTIMIZATION_LEASE_TRANSPORT_ENABLED:-true}"
optimization_mode="${STOCKTRADER_OPTIMIZATION_MODE:-Remote}"
optimization_mode_label="$(printf '%s' "$optimization_mode" | tr '[:upper:]' '[:lower:]')"
optimization_worker_replicas="${STOCKTRADER_OPTIMIZATION_WORKER_REPLICAS:-2}"
optimization_worker_concurrency="${STOCKTRADER_OPTIMIZATION_WORKER_CONCURRENCY:-2}"
market_data_mode="${STOCKTRADER_MARKET_DATA_MODE:-Local}"
market_data_shadow_backfill="${STOCKTRADER_MARKET_DATA_SHADOW_BACKFILL_ENABLED:-false}"
market_data_dir="${STOCKTRADER_MARKET_DATA_DIR:-}"
market_data_tls_generation="${STOCKTRADER_MARKET_DATA_TLS_GENERATION:-}"
market_data_server_tls_secret=""
market_data_client_tls_secret=""
ml_training_dir="${STOCKTRADER_ML_TRAINING_DIR:-}"
ml_training_tls_generation="${STOCKTRADER_ML_TRAINING_TLS_GENERATION:-}"
ml_training_server_tls_secret=""
ml_training_client_tls_secret=""
ml_training_mode="${STOCKTRADER_ML_TRAINING_MODE:-Local}"
trading_core_dir="${STOCKTRADER_TRADING_CORE_DIR:-}"
trading_core_tls_generation="${STOCKTRADER_TRADING_CORE_TLS_GENERATION:-}"
trading_core_server_tls_secret=""
trading_core_client_tls_secret=""
trading_core_mode="${STOCKTRADER_TRADING_CORE_MODE:-Projection}"

deploy_api=false
deploy_desktop=false
deploy_worker=false
deploy_market_data=false
deploy_ml_training=false
deploy_trading_core=false
case "$deploy_scope" in
  all) deploy_api=true; deploy_desktop=true; deploy_worker=true; deploy_market_data=true; deploy_ml_training=true; deploy_trading_core=true ;;
  api) deploy_api=true ;;
  desktop) deploy_desktop=true ;;
  optimization-worker) deploy_worker=true ;;
  market-data) deploy_market_data=true ;;
  ml-training) deploy_ml_training=true ;;
  trading-core) deploy_trading_core=true ;;
  *)
    echo "STOCKTRADER_DEPLOY_SCOPE must be all, api, desktop, optimization-worker, market-data, ml-training, or trading-core." >&2
    exit 1
    ;;
esac

if $deploy_api; then
  data_dir="${STOCKTRADER_DATA_DIR:?Set STOCKTRADER_DATA_DIR to the absolute host data directory}"
fi
if $deploy_desktop; then
  stocktrader_host="${STOCKTRADER_HOST:?Set STOCKTRADER_HOST to the public hostname}"
fi
if $deploy_market_data; then
  market_data_dir="${market_data_dir:?Set STOCKTRADER_MARKET_DATA_DIR to the absolute host data directory}"
fi
if $deploy_ml_training; then
  ml_training_dir="${ml_training_dir:?Set STOCKTRADER_ML_TRAINING_DIR to the absolute host data directory}"
fi
if $deploy_trading_core; then
  trading_core_dir="${trading_core_dir:?Set STOCKTRADER_TRADING_CORE_DIR to the absolute host data directory}"
fi
if $deploy_api || $deploy_worker; then
  if [[ -z "$tls_generation" ]]; then
    tls_generation="$(sudo k3s kubectl -n stocktrader get configmap \
      stocktrader-optimization-worker-tls-active \
      -o jsonpath='{.data.generation}' 2>/dev/null || true)"
  fi
  if [[ ! "$tls_generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]]; then
    echo "No valid Optimization Worker TLS generation is active." >&2
    echo "Run scripts/rotate-optimization-worker-tls.sh first." >&2
    exit 1
  fi
  server_tls_secret="stocktrader-optimization-worker-server-tls-$tls_generation"
  client_tls_secret="stocktrader-optimization-worker-client-tls-$tls_generation"
fi
if $deploy_api || $deploy_ml_training; then
  if [[ -z "$ml_training_tls_generation" ]]; then
    ml_training_tls_generation="$(sudo k3s kubectl -n stocktrader get configmap \
      stocktrader-ml-training-tls-active -o jsonpath='{.data.generation}' 2>/dev/null || true)"
  fi
  if [[ ! "$ml_training_tls_generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]]; then
    echo "No valid ML Training TLS generation is active." >&2
    echo "Run scripts/rotate-ml-training-tls.sh first." >&2
    exit 1
  fi
  ml_training_server_tls_secret="stocktrader-ml-training-server-tls-$ml_training_tls_generation"
  ml_training_client_tls_secret="stocktrader-ml-training-client-tls-$ml_training_tls_generation"
fi
if $deploy_api || $deploy_market_data; then
  if [[ -z "$market_data_tls_generation" ]]; then
    market_data_tls_generation="$(sudo k3s kubectl -n stocktrader get configmap \
      stocktrader-market-data-tls-active -o jsonpath='{.data.generation}' 2>/dev/null || true)"
  fi
  if [[ ! "$market_data_tls_generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]]; then
    echo "No valid Market Data TLS generation is active." >&2
    echo "Run scripts/rotate-market-data-tls.sh first." >&2
    exit 1
  fi
  market_data_server_tls_secret="stocktrader-market-data-server-tls-$market_data_tls_generation"
  market_data_client_tls_secret="stocktrader-market-data-client-tls-$market_data_tls_generation"
fi
if $deploy_api || $deploy_trading_core; then
  if [[ -z "$trading_core_tls_generation" ]]; then
    trading_core_tls_generation="$(sudo k3s kubectl -n stocktrader get configmap \
      stocktrader-trading-core-tls-active -o jsonpath='{.data.generation}' 2>/dev/null || true)"
  fi
  if [[ ! "$trading_core_tls_generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]]; then
    echo "No valid Trading Core TLS generation is active." >&2
    echo "Run scripts/rotate-trading-core-tls.sh first." >&2
    exit 1
  fi
  trading_core_server_tls_secret="stocktrader-trading-core-server-tls-$trading_core_tls_generation"
  trading_core_client_tls_secret="stocktrader-trading-core-client-tls-$trading_core_tls_generation"
fi

if $deploy_api && { [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; }; then
  echo "STOCKTRADER_DATA_DIR must be a safe absolute path below the filesystem root." >&2
  exit 1
fi
if $deploy_market_data && { [[ ! "$market_data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$market_data_dir" == "/" ]]; }; then
  echo "STOCKTRADER_MARKET_DATA_DIR must be a safe absolute path below the filesystem root." >&2
  exit 1
fi
if $deploy_ml_training && { [[ ! "$ml_training_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$ml_training_dir" == "/" ]]; }; then
  echo "STOCKTRADER_ML_TRAINING_DIR must be a safe absolute path below the filesystem root." >&2
  exit 1
fi
if $deploy_trading_core && { [[ ! "$trading_core_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$trading_core_dir" == "/" ]]; }; then
  echo "STOCKTRADER_TRADING_CORE_DIR must be a safe absolute path below the filesystem root." >&2
  exit 1
fi
if { $deploy_api || $deploy_market_data; } && [[ "$market_data_mode" != "Local" && "$market_data_mode" != "Shadow" && "$market_data_mode" != "Remote" ]]; then
  echo "STOCKTRADER_MARKET_DATA_MODE must be Local, Shadow, or Remote." >&2
  exit 1
fi
if [[ "$market_data_shadow_backfill" != "true" && "$market_data_shadow_backfill" != "false" ]]; then
  echo "STOCKTRADER_MARKET_DATA_SHADOW_BACKFILL_ENABLED must be true or false." >&2
  exit 1
fi
if { $deploy_api || $deploy_ml_training; } \
  && [[ "$ml_training_mode" != "Local" && "$ml_training_mode" != "Shadow" && "$ml_training_mode" != "Remote" ]]; then
  echo "STOCKTRADER_ML_TRAINING_MODE must be Local, Shadow, or Remote." >&2
  exit 1
fi
if { $deploy_api || $deploy_trading_core; } \
  && [[ "$trading_core_mode" != "Local" && "$trading_core_mode" != "Projection" \
    && "$trading_core_mode" != "Shadow" && "$trading_core_mode" != "Remote" ]]; then
  echo "STOCKTRADER_TRADING_CORE_MODE must be Local, Projection, Shadow, or Remote." >&2
  exit 1
fi
if $deploy_trading_core && [[ "$trading_core_mode" == "Local" ]]; then
  echo "A deployed Trading Core must start in Projection, Shadow, or Remote mode." >&2
  exit 1
fi

if $deploy_api && [[ "$lease_transport_enabled" != "true" && "$lease_transport_enabled" != "false" ]]; then
  echo "STOCKTRADER_OPTIMIZATION_LEASE_TRANSPORT_ENABLED must be true or false." >&2
  exit 1
fi
if { $deploy_api || $deploy_worker; } && [[ "$optimization_mode" != "Shadow" && "$optimization_mode" != "Remote" ]]; then
  echo "STOCKTRADER_OPTIMIZATION_MODE must be Shadow or Remote." >&2
  exit 1
fi
if { $deploy_api || $deploy_worker; } \
  && { [[ ! "$optimization_worker_replicas" =~ ^[1-9][0-9]*$ ]] \
    || (( optimization_worker_replicas > 16 )); }; then
  echo "STOCKTRADER_OPTIMIZATION_WORKER_REPLICAS must be between 1 and 16." >&2
  exit 1
fi
if $deploy_api \
  && { [[ ! "$optimization_worker_concurrency" =~ ^[1-9][0-9]*$ ]] \
    || (( optimization_worker_concurrency > 16 )); }; then
  echo "STOCKTRADER_OPTIMIZATION_WORKER_CONCURRENCY must be between 1 and 16." >&2
  exit 1
fi
if $deploy_api && [[ "$optimization_mode" == "Remote" && "$lease_transport_enabled" != "true" ]]; then
  echo "Remote optimization mode requires lease transport to be enabled." >&2
  exit 1
fi

if $deploy_desktop && { [[ ! "$stocktrader_host" =~ ^[A-Za-z0-9.-]+$ ]] || [[ "$stocktrader_host" != *.* ]]; }; then
  echo "STOCKTRADER_HOST must be a valid DNS hostname." >&2
  exit 1
fi

cleanup() {
  sudo buildah rm "$migration_container" >/dev/null 2>&1 || true
  sudo rm -f -- "$archive_dir/api.tar" "$archive_dir/desktop.tar" "$archive_dir/worker.tar" "$archive_dir/market-data.tar" "$archive_dir/ml-training.tar" "$archive_dir/trading-core.tar"
  rmdir "$archive_dir" 2>/dev/null || true
}
trap cleanup EXIT

sudo k3s kubectl apply -f k8s/namespace.yaml
if $deploy_api || $deploy_worker; then
  sudo k3s kubectl apply -f k8s/network-policy-optimization-worker.yaml
fi
if $deploy_api || $deploy_market_data; then
  sudo k3s kubectl apply -f k8s/network-policy-market-data.yaml
fi
if $deploy_api || $deploy_ml_training; then
  sudo k3s kubectl apply -f k8s/network-policy-ml-training.yaml
fi
if $deploy_api || $deploy_trading_core; then
  sudo k3s kubectl apply -f k8s/network-policy-trading-core.yaml
fi
if $deploy_api && ! sudo k3s kubectl -n stocktrader get secret stocktrader-alpaca >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-alpaca." >&2
  echo "Create it from k8s/secret.example.yaml before deploying." >&2
  exit 1
fi
if { $deploy_api || $deploy_worker; } \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-optimization-worker-auth >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-optimization-worker-auth." >&2
  echo "Create it from k8s/secret-optimization-worker.example.yaml before deploying." >&2
  exit 1
fi
for tls_secret in "$server_tls_secret" "$client_tls_secret"; do
  if { $deploy_api || $deploy_worker; } \
    && ! sudo k3s kubectl -n stocktrader get secret "$tls_secret" >/dev/null 2>&1; then
    echo "Missing Kubernetes secret stocktrader/$tls_secret." >&2
    echo "Create or rotate it with scripts/rotate-optimization-worker-tls.sh." >&2
    exit 1
  fi
done
if { $deploy_api || $deploy_ml_training; } \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-ml-training-auth >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-ml-training-auth." >&2
  echo "Create it from k8s/secret-ml-training.example.yaml before deploying." >&2
  exit 1
fi
for tls_secret in "$ml_training_server_tls_secret" "$ml_training_client_tls_secret"; do
  if { $deploy_api || $deploy_ml_training; } \
    && ! sudo k3s kubectl -n stocktrader get secret "$tls_secret" >/dev/null 2>&1; then
    echo "Missing Kubernetes secret stocktrader/$tls_secret." >&2
    echo "Create or rotate it with scripts/rotate-ml-training-tls.sh." >&2
    exit 1
  fi
done
if { $deploy_api || $deploy_market_data; } \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-market-data-auth >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-market-data-auth." >&2
  echo "Create it from k8s/secret-market-data.example.yaml before deploying." >&2
  exit 1
fi
if $deploy_market_data \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-market-data-providers >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-market-data-providers." >&2
  exit 1
fi
for tls_secret in "$market_data_server_tls_secret" "$market_data_client_tls_secret"; do
  if { $deploy_api || $deploy_market_data; } \
    && ! sudo k3s kubectl -n stocktrader get secret "$tls_secret" >/dev/null 2>&1; then
    echo "Missing Kubernetes secret stocktrader/$tls_secret." >&2
    echo "Create or rotate it with scripts/rotate-market-data-tls.sh." >&2
    exit 1
  fi
done
if { $deploy_api || $deploy_trading_core; } \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-trading-core-auth >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-trading-core-auth." >&2
  echo "Create it from k8s/secret-trading-core.example.yaml before deploying." >&2
  exit 1
fi
if $deploy_trading_core \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-trading-core-encryption >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-trading-core-encryption." >&2
  echo "Create it from k8s/secret-trading-core.example.yaml before deploying." >&2
  exit 1
fi
for tls_secret in "$trading_core_server_tls_secret" "$trading_core_client_tls_secret"; do
  if { $deploy_api || $deploy_trading_core; } \
    && ! sudo k3s kubectl -n stocktrader get secret "$tls_secret" >/dev/null 2>&1; then
    echo "Missing Kubernetes secret stocktrader/$tls_secret." >&2
    echo "Create or rotate it with scripts/rotate-trading-core-tls.sh." >&2
    exit 1
  fi
done

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
if $deploy_market_data; then
  sudo buildah bud --layers -f Dockerfile.market-data -t "$market_data_image" .
  sudo buildah push "$market_data_image" "oci-archive:$archive_dir/market-data.tar:$market_data_image"
fi
if $deploy_ml_training; then
  sudo buildah bud --layers -f Dockerfile.ml-training -t "$ml_training_image" .
  sudo buildah push "$ml_training_image" "oci-archive:$archive_dir/ml-training.tar:$ml_training_image"
fi
if $deploy_trading_core; then
  sudo buildah bud --layers -f Dockerfile.trading-core -t "$trading_core_image" .
  sudo buildah push "$trading_core_image" "oci-archive:$archive_dir/trading-core.tar:$trading_core_image"
fi

if $deploy_api && [[ "$market_data_mode" == "Local" ]] \
  && sudo k3s kubectl -n stocktrader get deployment stocktrader-api >/dev/null 2>&1; then
  current_market_data_mode="$(sudo k3s kubectl -n stocktrader get deployment stocktrader-api \
    -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="MarketDataTransport__Mode")].value}')"
  if [[ "$current_market_data_mode" == "Remote" ]]; then
    sudo install -d -m 0750 "$data_dir/backups"
    if ! command -v sqlite3 >/dev/null 2>&1; then
      echo "sqlite3 is required to back up the compatibility database before rollback." >&2
      exit 1
    fi
    rollback_backup="$data_dir/backups/stocktrader-before-market-data-rollback-${release_tag}-$(date -u +%Y%m%dT%H%M%SZ).db"
    sudo sqlite3 "$data_dir/stocktrader.db" ".backup '$rollback_backup'"
    sudo sqlite3 "$rollback_backup" "PRAGMA quick_check;" | grep -qx ok
    echo "Pre-projection compatibility backup: $rollback_backup"
    echo "Projecting authoritative Market Data into the Local compatibility store before rollback."
    sudo k3s kubectl -n stocktrader exec deployment/stocktrader-api -- \
      dotnet StockTrader.dll --project-market-data-rollback
  fi
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
if $deploy_market_data; then
  # K3s hostPath does not reliably apply fsGroup ownership. The Market Data
  # image runs as the fixed non-root UID/GID 1654 and exclusively owns this path.
  sudo install -d -o 1654 -g 1654 -m 0750 "$market_data_dir"
  sudo install -d -o 1654 -g 1654 -m 0750 "$market_data_dir/backups"
  if sudo test -f "$market_data_dir/marketdata.db"; then
    if ! command -v sqlite3 >/dev/null 2>&1; then
      echo "sqlite3 is required to back up the Market Data database." >&2
      exit 1
    fi
    market_data_backup="$market_data_dir/backups/marketdata-pre-${release_tag}-$(date -u +%Y%m%dT%H%M%SZ).db"
    sudo sqlite3 "$market_data_dir/marketdata.db" ".backup '$market_data_backup'"
    sudo sqlite3 "$market_data_backup" "PRAGMA quick_check;" | grep -qx ok
    echo "Market Data database backup: $market_data_backup"
  fi
fi
if $deploy_ml_training; then
  sudo install -d -o 1654 -g 1654 -m 0750 "$ml_training_dir"
  sudo install -d -o 1654 -g 1654 -m 0750 "$ml_training_dir/backups" "$ml_training_dir/artifacts"
  if sudo test -f "$ml_training_dir/jobs.db"; then
    ml_backup="$ml_training_dir/backups/jobs-pre-${release_tag}-$(date -u +%Y%m%dT%H%M%SZ).db"
    sudo sqlite3 "$ml_training_dir/jobs.db" ".backup '$ml_backup'"
    sudo sqlite3 "$ml_backup" "PRAGMA quick_check;" | grep -qx ok
    echo "ML Training database backup: $ml_backup"
  fi
fi
if $deploy_trading_core; then
  sudo install -d -o 1654 -g 1654 -m 0750 "$trading_core_dir"
  sudo install -d -o 1654 -g 1654 -m 0750 "$trading_core_dir/backups"
  if sudo test -f "$trading_core_dir/trading-core.db"; then
    trading_core_backup="$trading_core_dir/backups/trading-core-pre-${release_tag}-$(date -u +%Y%m%dT%H%M%SZ).db"
    sudo sqlite3 "$trading_core_dir/trading-core.db" ".backup '$trading_core_backup'"
    sudo sqlite3 "$trading_core_backup" "PRAGMA quick_check;" | grep -qx ok
    echo "Trading Core database backup: $trading_core_backup"
  fi
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
if $deploy_market_data; then
  sudo k3s ctr images import "$archive_dir/market-data.tar"
fi
if $deploy_ml_training; then
  sudo k3s ctr images import "$archive_dir/ml-training.tar"
fi
if $deploy_trading_core; then
  sudo k3s ctr images import "$archive_dir/trading-core.tar"
fi

if $deploy_market_data; then
  sed -e "s|localhost/stock-trader/market-data:latest|$market_data_image|" \
    -e "s|__MARKET_DATA_DATA_DIR__|$market_data_dir|" \
    -e "s|__MARKET_DATA_SERVER_TLS_SECRET__|$market_data_server_tls_secret|" \
    -e "s|__MARKET_DATA_CLIENT_TLS_SECRET__|$market_data_client_tls_secret|" \
    k8s/deployment-market-data.yaml | sudo k3s kubectl apply -f -
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-market-data --timeout=300s
fi
if $deploy_ml_training; then
  sed -e "s|localhost/stock-trader/ml-training:latest|$ml_training_image|" \
    -e "s|__ML_TRAINING_DATA_DIR__|$ml_training_dir|" \
    -e "s|__ML_TRAINING_SERVER_TLS_SECRET__|$ml_training_server_tls_secret|" \
    -e "s|__ML_TRAINING_CLIENT_TLS_SECRET__|$ml_training_client_tls_secret|" \
    k8s/deployment-ml-training.yaml | sudo k3s kubectl apply -f -
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-ml-training --timeout=600s
fi
if $deploy_trading_core; then
  sed -e "s|localhost/stock-trader/trading-core:latest|$trading_core_image|" \
    -e "s|__TRADING_CORE_DATA_DIR__|$trading_core_dir|" \
    -e "s|__TRADING_CORE_SERVER_TLS_SECRET__|$trading_core_server_tls_secret|" \
    -e "s|__TRADING_CORE_CLIENT_TLS_SECRET__|$trading_core_client_tls_secret|" \
    -e "s|__TRADING_CORE_MODE__|$trading_core_mode|" \
    k8s/deployment-trading-core.yaml | sudo k3s kubectl apply -f -
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-trading-core --timeout=300s
fi

if $deploy_api; then
  sed -e "s|localhost/stock-trader/api:latest|$api_image|" \
    -e "s|__STOCKTRADER_DATA_DIR__|$data_dir|" k8s/deployment-api.yaml \
    | sed -e "s|__OPTIMIZATION_WORKER_SERVER_TLS_SECRET__|$server_tls_secret|" \
      -e "s|__OPTIMIZATION_WORKER_CLIENT_TLS_SECRET__|$client_tls_secret|" \
      -e "s|__OPTIMIZATION_WORKER_LEASE_TRANSPORT_ENABLED__|$lease_transport_enabled|" \
      -e "s|__OPTIMIZATION_WORKER_MODE__|$optimization_mode|" \
      -e "s|__OPTIMIZATION_WORKER_CONCURRENCY__|$optimization_worker_concurrency|" \
      -e "s|__MARKET_DATA_MODE__|$market_data_mode|" \
      -e "s|__MARKET_DATA_SHADOW_BACKFILL_ENABLED__|$market_data_shadow_backfill|" \
      -e "s|__MARKET_DATA_CLIENT_TLS_SECRET__|$market_data_client_tls_secret|" \
      -e "s|__ML_TRAINING_MODE__|$ml_training_mode|" \
      -e "s|__ML_TRAINING_CLIENT_TLS_SECRET__|$ml_training_client_tls_secret|" \
      -e "s|__TRADING_CORE_MODE__|$trading_core_mode|" \
      -e "s|__TRADING_CORE_CLIENT_TLS_SECRET__|$trading_core_client_tls_secret|" \
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
    | sed -e "s|__OPTIMIZATION_WORKER_CLIENT_TLS_SECRET__|$client_tls_secret|" \
      -e "s|__OPTIMIZATION_WORKER_MODE__|$optimization_mode|" \
      -e "s|__OPTIMIZATION_WORKER_MODE_LABEL__|$optimization_mode_label|" \
      -e "s|__OPTIMIZATION_WORKER_REPLICAS__|$optimization_worker_replicas|" \
    | sudo k3s kubectl apply -f -
fi
if $deploy_ml_training; then
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-ml-training --timeout=600s
fi
if $deploy_market_data; then
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-market-data --timeout=300s
fi
if $deploy_trading_core; then
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-trading-core --timeout=300s
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
$deploy_market_data && selected_apps+=(stocktrader-market-data)
$deploy_ml_training && selected_apps+=(stocktrader-ml-training)
$deploy_trading_core && selected_apps+=(stocktrader-trading-core)
sudo k3s kubectl -n stocktrader get deployment "${selected_apps[@]}"
selector="$(IFS=,; echo "${selected_apps[*]}")"
sudo k3s kubectl -n stocktrader get pods -l "app in (${selector})"
