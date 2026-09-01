#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

release_tag="${1:-$(git rev-parse --short=12 HEAD)}"
deploy_scope="${STOCKTRADER_DEPLOY_SCOPE:-all}"
api_local_image="localhost/stock-trader/api-local:architecture-${release_tag}"
api_remote_image="localhost/stock-trader/api-remote:architecture-${release_tag}"
api_image="$api_local_image"
desktop_image="localhost/stock-trader/desktop:architecture-${release_tag}"
worker_image="localhost/stock-trader/optimization-worker:architecture-${release_tag}"
market_data_image="localhost/stock-trader/market-data:architecture-${release_tag}"
ml_training_image="localhost/stock-trader/ml-training:architecture-${release_tag}"
trading_core_remote_image="localhost/stock-trader/trading-core:architecture-${release_tag}"
trading_core_shadow_image="localhost/stock-trader/trading-core-shadow:architecture-${release_tag}"
acceptance_core_image="localhost/stock-trader/trading-core-acceptance:architecture-${release_tag}"
acceptance_broker_image="localhost/stock-trader/trading-core-broker-emulator:architecture-${release_tag}"
acceptance_driver_image="localhost/stock-trader/trading-core-acceptance-driver:architecture-${release_tag}"
trading_core_image="$trading_core_shadow_image"
archive_dir="$(mktemp -d /tmp/stocktrader-deploy.XXXXXX)"
acceptance_tls_dir=""
source_archive_dir="${STOCKTRADER_IMAGE_ARCHIVE_DIR:-}"
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
market_data_trading_core_client_tls_secret=""
market_data_acceptance_client_tls_secret=""
ml_training_dir="${STOCKTRADER_ML_TRAINING_DIR:-}"
ml_training_tls_generation="${STOCKTRADER_ML_TRAINING_TLS_GENERATION:-}"
ml_training_server_tls_secret=""
ml_training_client_tls_secret=""
ml_training_mode="${STOCKTRADER_ML_TRAINING_MODE:-Local}"
trading_core_dir="${STOCKTRADER_TRADING_CORE_DIR:-}"
trading_core_tls_generation="${STOCKTRADER_TRADING_CORE_TLS_GENERATION:-}"
trading_core_server_tls_secret=""
trading_core_client_tls_secret=""
edge_cutover_server_tls_secret=""
cutover_coordinator_client_tls_secret=""
trading_core_encryption_generation="${STOCKTRADER_TRADING_CORE_ENCRYPTION_GENERATION:-}"
trading_core_encryption_secret=""
trading_core_mode="${STOCKTRADER_TRADING_CORE_MODE:-Projection}"
if [[ "$trading_core_mode" == "Remote" ]]; then
  api_image="$api_remote_image"
  trading_core_image="$trading_core_remote_image"
fi

deploy_api=false
deploy_desktop=false
deploy_worker=false
deploy_market_data=false
deploy_ml_training=false
deploy_trading_core=false
deploy_transition=false
deploy_acceptance=false
transition_direction=""
case "$deploy_scope" in
  all) deploy_api=true; deploy_desktop=true; deploy_worker=true; deploy_market_data=true; deploy_ml_training=true; deploy_trading_core=true ;;
  api) deploy_api=true ;;
  desktop) deploy_desktop=true ;;
  optimization-worker) deploy_worker=true ;;
  market-data) deploy_market_data=true ;;
  ml-training) deploy_ml_training=true ;;
  trading-core) deploy_trading_core=true ;;
  trading-core-shadow-candidate) deploy_api=true; deploy_trading_core=true; trading_core_mode="Shadow"; api_image="$api_local_image"; trading_core_image="$trading_core_shadow_image" ;;
  trading-core-cutover) deploy_transition=true; transition_direction="Cutover" ;;
  trading-core-rollback) deploy_transition=true; transition_direction="Rollback" ;;
  trading-core-recutover) deploy_transition=true; transition_direction="Cutover" ;;
  trading-core-acceptance) deploy_acceptance=true ;;
  *)
    echo "STOCKTRADER_DEPLOY_SCOPE must be all, api, desktop, optimization-worker, market-data, ml-training, trading-core, trading-core-acceptance, trading-core-shadow-candidate, trading-core-cutover, trading-core-rollback, or trading-core-recutover." >&2
    exit 1
    ;;
esac

if $deploy_api || $deploy_transition; then
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
if $deploy_api || $deploy_market_data || $deploy_trading_core || $deploy_acceptance; then
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
  market_data_trading_core_client_tls_secret="stocktrader-market-data-trading-core-client-tls-$market_data_tls_generation"
  market_data_acceptance_client_tls_secret="stocktrader-market-data-acceptance-client-tls-$market_data_tls_generation"
fi
if $deploy_api || $deploy_trading_core || $deploy_transition; then
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
  edge_cutover_server_tls_secret="stocktrader-edge-cutover-server-tls-$trading_core_tls_generation"
  cutover_coordinator_client_tls_secret="stocktrader-cutover-coordinator-client-tls-$trading_core_tls_generation"
fi
if $deploy_trading_core; then
  if [[ -z "$trading_core_encryption_generation" ]]; then
    trading_core_encryption_generation="$(sudo k3s kubectl -n stocktrader get configmap \
      stocktrader-trading-core-encryption-active -o jsonpath='{.data.generation}' 2>/dev/null || true)"
  fi
  trading_core_encryption_generation="${trading_core_encryption_generation:-legacy}"
  if [[ "$trading_core_encryption_generation" != "legacy" \
    && ! "$trading_core_encryption_generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]]; then
    echo "STOCKTRADER_TRADING_CORE_ENCRYPTION_GENERATION must be legacy or 1-14 lowercase letters, digits, or hyphens." >&2
    exit 1
  fi
  if [[ "$trading_core_encryption_generation" == "legacy" ]]; then
    trading_core_encryption_secret="stocktrader-trading-core-encryption"
  else
    trading_core_encryption_secret="stocktrader-trading-core-encryption-$trading_core_encryption_generation"
  fi
fi

if { $deploy_api || $deploy_transition; } && { [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; }; then
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
  sudo rm -f -- "$archive_dir/api.tar" "$archive_dir/desktop.tar" \
    "$archive_dir/worker.tar" "$archive_dir/market-data.tar" \
    "$archive_dir/ml-training.tar" "$archive_dir/trading-core.tar" \
    "$archive_dir/deployment-api.yaml" "$archive_dir/deployment-api.yaml.remote" \
    "$archive_dir/trading-core-transition.yaml" "$archive_dir/acceptance-run.yaml" \
    "$archive_dir/acceptance-scenario.yaml" "$archive_dir/acceptance-manifest.yaml" \
    "$archive_dir/scenario-definition.json"
  if [[ -n "$acceptance_tls_dir" && -d "$acceptance_tls_dir" ]]; then
    sudo rm -f -- "$acceptance_tls_dir"/*
    rmdir "$acceptance_tls_dir" 2>/dev/null || true
  fi
  rmdir "$archive_dir" 2>/dev/null || true
}
trap cleanup EXIT

prepare_image() {
  local dockerfile="$1"
  local image="$2"
  local archive_name="$3"
  local target_stage="${4:-}"
  local target="$archive_dir/$archive_name"
  if [[ -n "$source_archive_dir" ]]; then
    local source="$source_archive_dir/$archive_name"
    local image_archive="${image##*/}"
    image_archive="${image_archive%%:*}.tar"
    if [[ -f "$source_archive_dir/$image_archive" ]]; then
      source="$source_archive_dir/$image_archive"
    fi
    if [[ ! -f "$source" ]]; then
      echo "Missing prebuilt image archive: $source" >&2
      exit 1
    fi
    cp -- "$source" "$target"
    return
  fi
  local target_args=()
  [[ -n "$target_stage" ]] && target_args=(--target "$target_stage")
  sudo buildah bud --layers "${target_args[@]}" -f "$dockerfile" -t "$image" .
  sudo buildah push "$image" "oci-archive:$target:$image"
}

metadata_value() {
  local key="$1"
  local metadata="$source_archive_dir/stage5-metadata.env"
  sed -n "s/^${key}=//p" "$metadata" | head -n 1
}

oci_archive_digest() {
  local archive="$1"
  local digest
  digest="$(tar -xOf "$archive" index.json \
    | sed -nE 's/.*"digest"[[:space:]]*:[[:space:]]*"(sha256:[0-9a-f]{64})".*/\1/p' \
    | head -n 1)"
  [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] || {
    echo "Cannot derive OCI manifest digest from $archive" >&2
    exit 1
  }
  printf '%s' "$digest"
}

wait_acceptance_job() {
  local namespace="$1"
  local job="$2"
  local attempts=0
  while (( attempts < 300 )); do
    local complete failed
    complete="$(sudo k3s kubectl -n "$namespace" get job "$job" \
      -o jsonpath='{.status.conditions[?(@.type=="Complete")].status}' 2>/dev/null || true)"
    failed="$(sudo k3s kubectl -n "$namespace" get job "$job" \
      -o jsonpath='{.status.conditions[?(@.type=="Failed")].status}' 2>/dev/null || true)"
    [[ "$complete" == "True" ]] && return 0
    [[ "$failed" == "True" ]] && return 1
    sleep 2
    attempts=$((attempts + 1))
  done
  return 1
}

sudo k3s kubectl apply -f k8s/namespace.yaml
if $deploy_api || $deploy_transition; then
  sudo k3s kubectl apply -f k8s/network-policy-api.yaml
fi
if $deploy_api || $deploy_worker; then
  sudo k3s kubectl apply -f k8s/network-policy-optimization-worker.yaml
fi
if $deploy_api || $deploy_market_data || $deploy_acceptance; then
  sudo k3s kubectl apply -f k8s/network-policy-market-data.yaml
fi
if $deploy_api || $deploy_ml_training; then
  sudo k3s kubectl apply -f k8s/network-policy-ml-training.yaml
fi
if $deploy_api || $deploy_trading_core || $deploy_transition; then
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
if $deploy_market_data \
  && ! sudo k3s kubectl -n stocktrader get secret stocktrader-market-data-providers >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-market-data-providers." >&2
  exit 1
fi
for tls_secret in "$market_data_server_tls_secret" "$market_data_client_tls_secret" "$market_data_trading_core_client_tls_secret"; do
  if { $deploy_api || $deploy_market_data || $deploy_trading_core; } \
    && ! sudo k3s kubectl -n stocktrader get secret "$tls_secret" >/dev/null 2>&1; then
    echo "Missing Kubernetes secret stocktrader/$tls_secret." >&2
    echo "Create or rotate it with scripts/rotate-market-data-tls.sh." >&2
    exit 1
  fi
done
if $deploy_acceptance \
  && ! sudo k3s kubectl -n stocktrader get secret "$market_data_acceptance_client_tls_secret" >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/$market_data_acceptance_client_tls_secret." >&2
  echo "Rotate and redeploy Market Data TLS before isolated acceptance." >&2
  exit 1
fi
if $deploy_trading_core \
  && ! sudo k3s kubectl -n stocktrader get secret "$trading_core_encryption_secret" >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/$trading_core_encryption_secret." >&2
  echo "Create it from k8s/secret-trading-core.example.yaml before deploying." >&2
  exit 1
fi
for tls_secret in "$trading_core_server_tls_secret" "$trading_core_client_tls_secret" \
  "$edge_cutover_server_tls_secret" "$cutover_coordinator_client_tls_secret"; do
  if { $deploy_api || $deploy_trading_core || $deploy_transition; } \
    && ! sudo k3s kubectl -n stocktrader get secret "$tls_secret" >/dev/null 2>&1; then
    echo "Missing Kubernetes secret stocktrader/$tls_secret." >&2
    echo "Create or rotate it with scripts/rotate-trading-core-tls.sh." >&2
    exit 1
  fi
done

if $deploy_acceptance; then
  if [[ -z "$source_archive_dir" || ! -d "$source_archive_dir" \
    || ! -f "$source_archive_dir/SHA256SUMS" \
    || ! -f "$source_archive_dir/stage5-metadata.env" ]]; then
    echo "Trading Core acceptance requires a verified off-node stage5 archive directory." >&2
    exit 1
  fi
  (cd "$source_archive_dir" && sha256sum -c SHA256SUMS)
  kubernetes_api_cidr="${STOCKTRADER_KUBERNETES_API_CIDR:?Set STOCKTRADER_KUBERNETES_API_CIDR, for example 10.43.0.1/32}"
  acceptance_output_dir="${STOCKTRADER_ACCEPTANCE_OUTPUT_DIR:?Set STOCKTRADER_ACCEPTANCE_OUTPUT_DIR}"
  acceptance_run_id="${STOCKTRADER_ACCEPTANCE_RUN_ID:-${release_tag}-$(date -u +%Y%m%d%H%M%S)}"
  if [[ ! "$acceptance_run_id" =~ ^[a-z0-9][a-z0-9-]{0,39}$ ]]; then
    echo "STOCKTRADER_ACCEPTANCE_RUN_ID must be 1-40 lowercase letters, digits, or hyphens." >&2
    exit 1
  fi
  acceptance_namespace="stocktrader-acceptance-$acceptance_run_id"
  if sudo k3s kubectl get namespace "$acceptance_namespace" >/dev/null 2>&1; then
    echo "Acceptance namespace already exists: $acceptance_namespace" >&2
    exit 1
  fi
  for archive_name in trading-core-acceptance.tar trading-core-broker-emulator.tar \
      trading-core-acceptance-driver.tar; do
    [[ -f "$source_archive_dir/$archive_name" ]] || {
      echo "Missing stage5 image archive: $source_archive_dir/$archive_name" >&2
      exit 1
    }
    sudo k3s ctr images import "$source_archive_dir/$archive_name"
  done

  rendered_run="$archive_dir/acceptance-run.yaml"
  sed -e "s|__ACCEPTANCE_NAMESPACE__|$acceptance_namespace|g" \
    -e "s|__RUN_ID__|$acceptance_run_id|g" \
    -e "s|__KUBERNETES_API_CIDR__|$kubernetes_api_cidr|g" \
    k8s/trading-core-acceptance-run.yaml > "$rendered_run"
  sudo k3s kubectl apply -f "$rendered_run"

  acceptance_tls_dir="$(mktemp -d /tmp/stocktrader-acceptance-tls.XXXXXX)"
  sudo chown "$(id -u):$(id -g)" "$acceptance_tls_dir"
  chmod 700 "$acceptance_tls_dir"
  openssl genrsa -out "$acceptance_tls_dir/ca.key" 3072 >/dev/null 2>&1
  openssl req -x509 -new -sha256 -days 7 -key "$acceptance_tls_dir/ca.key" \
    -out "$acceptance_tls_dir/ca.crt" -subj "/CN=StockTrader Acceptance $acceptance_run_id" >/dev/null 2>&1
  issue_acceptance_certificate() {
    local name="$1" common_name="$2" san="$3" eku="$4"
    openssl genrsa -out "$acceptance_tls_dir/$name.key" 2048 >/dev/null 2>&1
    openssl req -new -sha256 -key "$acceptance_tls_dir/$name.key" \
      -out "$acceptance_tls_dir/$name.csr" -subj "/CN=$common_name" >/dev/null 2>&1
    printf '%s\n' 'basicConstraints=critical,CA:FALSE' \
      'keyUsage=critical,digitalSignature,keyEncipherment' \
      "extendedKeyUsage=$eku" "subjectAltName=DNS:$san" > "$acceptance_tls_dir/$name.ext"
    openssl x509 -req -sha256 -days 7 -in "$acceptance_tls_dir/$name.csr" \
      -CA "$acceptance_tls_dir/ca.crt" -CAkey "$acceptance_tls_dir/ca.key" \
      -CAcreateserial -out "$acceptance_tls_dir/$name.crt" \
      -extfile "$acceptance_tls_dir/$name.ext" >/dev/null 2>&1
  }
  issue_acceptance_certificate broker acceptance-broker acceptance-broker serverAuth
  issue_acceptance_certificate core acceptance-core acceptance-core serverAuth
  issue_acceptance_certificate driver acceptance-driver \
    "acceptance-driver.$acceptance_run_id.stocktrader.internal" clientAuth
  issue_acceptance_certificate core-client acceptance-trading-core \
    "acceptance-trading-core.$acceptance_run_id.stocktrader.internal" clientAuth
  for item in "acceptance-broker-server-tls:broker" \
      "acceptance-core-server-tls:core" \
      "acceptance-driver-client-tls:driver" \
      "acceptance-core-client-tls:core-client"; do
    secret="${item%%:*}"
    certificate="${item##*:}"
    sudo k3s kubectl -n "$acceptance_namespace" create secret generic "$secret" \
      --from-file=tls.crt="$acceptance_tls_dir/$certificate.crt" \
      --from-file=tls.key="$acceptance_tls_dir/$certificate.key" \
      --from-file=ca.crt="$acceptance_tls_dir/ca.crt"
  done
  openssl rand -base64 32 > "$acceptance_tls_dir/encryption-key"
  sudo k3s kubectl -n "$acceptance_namespace" create secret generic acceptance-encryption \
    --from-file=encryption-key="$acceptance_tls_dir/encryption-key"
  for key in tls.crt tls.key ca.crt; do
    sudo k3s kubectl -n stocktrader get secret "$market_data_acceptance_client_tls_secret" \
      -o "jsonpath={.data.${key//./\\.}}" | base64 -d > "$acceptance_tls_dir/market-data-$key"
  done
  sudo k3s kubectl -n "$acceptance_namespace" create secret generic acceptance-market-data-client-tls \
    --from-file=tls.crt="$acceptance_tls_dir/market-data-tls.crt" \
    --from-file=tls.key="$acceptance_tls_dir/market-data-tls.key" \
    --from-file=ca.crt="$acceptance_tls_dir/market-data-ca.crt"

  scenario_failed=false
  scenario_codes=(
    completed-bar-downtime-replay duplicate-command-delivery command-identity-conflict
    broker-rejection-before-fill broker-timeout-before-submission-proof
    broker-accepted-then-timeout delayed-out-of-order-partial-fills
    cancellation-with-partial-fill contradictory-terminal-quantity duplicate-broker-response
    broker-outage-and-recovery trading-core-pod-loss edge-loss-autonomous-protection
    evaluated-range-evidence-correction accepted-resource-load
    isolated-cutover-and-rollback-generation
  )
  for scenario_code in "${scenario_codes[@]}"; do
    scenario_id="$(tr -d '-' < /proc/sys/kernel/random/uuid | cut -c1-20)"
    scenario_guid="$(cat /proc/sys/kernel/random/uuid)"
    printf '{"contractVersion":1,"scenarioCode":"%s","scenarioId":"%s","provider":"Yahoo","symbol":"AAPL","adjustmentMode":"Raw","market":"US","calendarVersion":"market-calendar-v1","requiredBars":50}\n' \
      "$scenario_code" "$scenario_guid" > "$archive_dir/scenario-definition.json"
    sudo k3s kubectl -n "$acceptance_namespace" create configmap \
      "acceptance-definition-$scenario_id" \
      --from-file=scenario.json="$archive_dir/scenario-definition.json"
    rendered_scenario="$archive_dir/acceptance-scenario.yaml"
    sed -e "s|__ACCEPTANCE_NAMESPACE__|$acceptance_namespace|g" \
      -e "s|__RUN_ID__|$acceptance_run_id|g" \
      -e "s|__SCENARIO_ID__|$scenario_id|g" \
      -e "s|__SCENARIO_CODE__|$scenario_code|g" \
      -e "s|__RELEASE_TAG__|$release_tag|g" \
      k8s/trading-core-acceptance-scenario.yaml > "$rendered_scenario"
    sudo k3s kubectl apply -f "$rendered_scenario"
    if ! sudo k3s kubectl -n "$acceptance_namespace" rollout status \
        "deployment/acceptance-broker-$scenario_id" --timeout=180s \
      || ! sudo k3s kubectl -n "$acceptance_namespace" rollout status \
        "deployment/acceptance-core-$scenario_id" --timeout=180s \
      || ! wait_acceptance_job "$acceptance_namespace" "acceptance-driver-$scenario_id"; then
      sudo k3s kubectl -n "$acceptance_namespace" logs \
        "job/acceptance-driver-$scenario_id" --all-containers=true || true
      scenario_failed=true
    fi
    sudo k3s kubectl -n "$acceptance_namespace" delete \
      "job/acceptance-driver-$scenario_id" \
      "deployment/acceptance-broker-$scenario_id" "deployment/acceptance-core-$scenario_id" \
      service/acceptance-broker service/acceptance-core \
      "configmap/acceptance-definition-$scenario_id" \
      "pvc/tc-$scenario_id" "pvc/broker-$scenario_id" --wait=true
    $scenario_failed && break
  done

  repository_commit="$(metadata_value REPOSITORY_COMMIT)"
  build_id="$(metadata_value BUILD_ID)"
  service_contracts_hash="$(metadata_value SERVICE_CONTRACTS_HASH)"
  engine_hash="$(metadata_value ENGINE_HASH)"
  trading_core_hash="$(metadata_value TRADING_CORE_HASH)"
  runtime_hash="$(metadata_value RUNTIME_HASH)"
  edge_image_digest="$(metadata_value EDGE_IMAGE_DIGEST)"
  edge_local_image_digest="$(metadata_value EDGE_LOCAL_IMAGE_DIGEST)"
  production_core_image_digest="$(metadata_value TRADING_CORE_IMAGE_DIGEST)"
  shadow_core_image_digest="$(metadata_value TRADING_CORE_SHADOW_IMAGE_DIGEST)"
  market_data_image_digest="$(metadata_value MARKET_DATA_IMAGE_DIGEST)"
  acceptance_core_image_digest="$(metadata_value ACCEPTANCE_CORE_IMAGE_DIGEST)"
  broker_emulator_image_digest="$(metadata_value BROKER_EMULATOR_IMAGE_DIGEST)"
  driver_image_digest="$(metadata_value DRIVER_IMAGE_DIGEST)"
  coordinator_image_digest="$(metadata_value COORDINATOR_IMAGE_DIGEST)"
  rollback_importer_image_digest="$(metadata_value ROLLBACK_IMPORTER_IMAGE_DIGEST)"
  for value in "$repository_commit" "$build_id" "$service_contracts_hash" "$engine_hash" \
      "$trading_core_hash" "$runtime_hash" "$edge_image_digest" \
      "$edge_local_image_digest" "$production_core_image_digest" \
      "$shadow_core_image_digest" "$market_data_image_digest" \
      "$acceptance_core_image_digest" "$broker_emulator_image_digest" \
      "$driver_image_digest" "$coordinator_image_digest" \
      "$rollback_importer_image_digest"; do
    [[ -n "$value" ]] || { echo "Incomplete stage5 metadata." >&2; exit 1; }
  done
  rendered_manifest="$archive_dir/acceptance-manifest.yaml"
  sed -e "s|__ACCEPTANCE_NAMESPACE__|$acceptance_namespace|g" \
    -e "s|__RUN_ID__|$acceptance_run_id|g" \
    -e "s|__RELEASE_TAG__|$release_tag|g" \
    -e "s|__REPOSITORY_COMMIT__|$repository_commit|g" \
    -e "s|__BUILD_ID__|$build_id|g" \
    -e "s|__EDGE_DIGEST__|$edge_image_digest|g" \
    -e "s|__EDGE_LOCAL_DIGEST__|$edge_local_image_digest|g" \
    -e "s|__TRADING_CORE_DIGEST__|$production_core_image_digest|g" \
    -e "s|__TRADING_CORE_SHADOW_DIGEST__|$shadow_core_image_digest|g" \
    -e "s|__MARKET_DATA_DIGEST__|$market_data_image_digest|g" \
    -e "s|__TRADING_CORE_ACCEPTANCE_DIGEST__|$acceptance_core_image_digest|g" \
    -e "s|__BROKER_EMULATOR_DIGEST__|$broker_emulator_image_digest|g" \
    -e "s|__DRIVER_DIGEST__|$driver_image_digest|g" \
    -e "s|__COORDINATOR_DIGEST__|$coordinator_image_digest|g" \
    -e "s|__ROLLBACK_IMPORTER_DIGEST__|$rollback_importer_image_digest|g" \
    -e "s|__SERVICE_CONTRACTS_HASH__|$service_contracts_hash|g" \
    -e "s|__ENGINE_HASH__|$engine_hash|g" \
    -e "s|__TRADING_CORE_HASH__|$trading_core_hash|g" \
    -e "s|__RUNTIME_HASH__|$runtime_hash|g" \
    k8s/trading-core-acceptance-manifest-job.yaml > "$rendered_manifest"
  sudo k3s kubectl apply -f "$rendered_manifest"
  if ! wait_acceptance_job "$acceptance_namespace" acceptance-manifest; then
    sudo k3s kubectl -n "$acceptance_namespace" logs job/acceptance-manifest || true
    exit 1
  fi
  sudo install -d -m 0750 "$acceptance_output_dir"
  manifest_pod="$(sudo k3s kubectl -n "$acceptance_namespace" get pod \
    -l job-name=acceptance-manifest -o jsonpath='{.items[0].metadata.name}')"
  manifest_output="$acceptance_output_dir/$acceptance_run_id.acceptance-manifest.json"
  sudo k3s kubectl -n "$acceptance_namespace" cp \
    "$manifest_pod:/manifest/acceptance-manifest.json" "$manifest_output"
  manifest_sha="$(sha256sum "$manifest_output" | awk '{print $1}')"
  printf '%s  %s\n' "$manifest_sha" "$(basename "$manifest_output")" \
    > "$manifest_output.sha256"
  sudo k3s kubectl label namespace "$acceptance_namespace" \
    "stocktrader.io/manifest-sha=$manifest_sha" --overwrite
  if grep -q '"passed"[[:space:]]*:[[:space:]]*true' "$manifest_output"; then
    [[ "$(sudo k3s kubectl get namespace "$acceptance_namespace" \
      -o jsonpath='{.metadata.labels.stocktrader\.io/run-id}')" == "$acceptance_run_id" ]] || exit 1
    sudo k3s kubectl delete namespace "$acceptance_namespace" --wait=true
    echo "Isolated acceptance passed: $manifest_output"
    exit 0
  fi
  echo "Isolated acceptance failed; retained namespace $acceptance_namespace and artifact $manifest_output" >&2
  exit 1
fi

if $deploy_transition; then
  if [[ -z "$source_archive_dir" || ! -d "$source_archive_dir" ]]; then
    echo "Trading Core transitions require STOCKTRADER_IMAGE_ARCHIVE_DIR from an off-node stage5 build." >&2
    exit 1
  fi
  transition_plan_file="${STOCKTRADER_TRANSITION_PLAN_FILE:?Set STOCKTRADER_TRANSITION_PLAN_FILE}"
  kubernetes_api_cidr="${STOCKTRADER_KUBERNETES_API_CIDR:?Set STOCKTRADER_KUBERNETES_API_CIDR, for example 10.43.0.1/32}"
  if [[ ! -f "$transition_plan_file" || ! -f "$source_archive_dir/SHA256SUMS" ]]; then
    echo "Transition plan or archive SHA256SUMS is missing." >&2
    exit 1
  fi
  (cd "$source_archive_dir" && sha256sum -c SHA256SUMS)
  transition_id="$(sed -n 's/.*"transitionId"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$transition_plan_file" | head -n 1)"
  plan_direction="$(sed -n 's/.*"direction"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$transition_plan_file" | head -n 1)"
  if [[ ! "$transition_id" =~ ^[0-9a-fA-F-]{36}$ || "$plan_direction" != "$transition_direction" ]]; then
    echo "Transition plan identity or direction does not match $deploy_scope." >&2
    exit 1
  fi
  archive_names=(api-local.tar api-remote.tar trading-core.tar trading-core-shadow.tar \
    trading-core-cutover-coordinator.tar edge-rollback-importer.tar)
  for archive_name in "${archive_names[@]}"; do
    if [[ ! -f "$source_archive_dir/$archive_name" ]]; then
      echo "Missing stage5 image archive: $source_archive_dir/$archive_name" >&2
      exit 1
    fi
    sudo k3s ctr images import "$source_archive_dir/$archive_name"
  done
  if sudo k3s kubectl -n stocktrader get job "stocktrader-cutover-$transition_id" >/dev/null 2>&1; then
    echo "Transition job already exists; refusing to mutate immutable transition $transition_id." >&2
    exit 1
  fi
  sudo k3s kubectl -n stocktrader create configmap "stocktrader-cutover-plan-$transition_id" \
    --from-file=transition-plan.json="$transition_plan_file"
  rendered_transition="$archive_dir/trading-core-transition.yaml"
  sed -e "s|__TRANSITION_ID__|$transition_id|g" \
    -e "s|__STOCKTRADER_DATA_DIR__|$data_dir|g" \
    -e "s|__CUTOVER_COORDINATOR_CLIENT_TLS_SECRET__|$cutover_coordinator_client_tls_secret|g" \
    -e "s|__KUBERNETES_API_CIDR__|$kubernetes_api_cidr|g" \
    -e "s|localhost/stock-trader/trading-core-cutover-coordinator:latest|localhost/stock-trader/trading-core-cutover-coordinator:architecture-$release_tag|g" \
    -e "s|localhost/stock-trader/edge-rollback-importer:latest|localhost/stock-trader/edge-rollback-importer:architecture-$release_tag|g" \
    k8s/job-trading-core-cutover-coordinator.yaml > "$rendered_transition"
  sudo k3s kubectl apply -f "$rendered_transition"
  if ! sudo k3s kubectl -n stocktrader wait --for=condition=complete \
      "job/stocktrader-cutover-$transition_id" --timeout=900s; then
    sudo k3s kubectl -n stocktrader logs "job/stocktrader-cutover-$transition_id" --all-containers=true || true
    exit 1
  fi
  sudo k3s kubectl -n stocktrader logs "job/stocktrader-cutover-$transition_id" --all-containers=true
  sudo k3s kubectl -n stocktrader get deployment stocktrader-api stocktrader-trading-core
  sudo k3s kubectl -n stocktrader get pods -l 'app in (stocktrader-api,stocktrader-trading-core)'
  exit 0
fi

if $deploy_api; then
  api_runtime_target="local"
  [[ "$trading_core_mode" == "Remote" ]] && api_runtime_target="remote"
  prepare_image Dockerfile.api "$api_image" api.tar "$api_runtime_target"
fi

if $deploy_desktop; then
  prepare_image Dockerfile.desktop "$desktop_image" desktop.tar
fi

if $deploy_worker; then
  prepare_image Dockerfile.optimization-worker "$worker_image" worker.tar
fi
if $deploy_market_data; then
  prepare_image Dockerfile.market-data "$market_data_image" market-data.tar
fi
if $deploy_ml_training; then
  prepare_image Dockerfile.ml-training "$ml_training_image" ml-training.tar
fi
if $deploy_trading_core; then
  if [[ "$trading_core_mode" == "Remote" ]]; then
    prepare_image Dockerfile.trading-core "$trading_core_image" trading-core.tar
  else
    prepare_image Dockerfile.trading-core-shadow "$trading_core_image" trading-core.tar
  fi
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
  trading_core_broker_capability="false"
  trading_core_broker_egress="false"
  trading_core_runtime_profile="trading-core-shadow"
  if [[ "$trading_core_mode" == "Remote" ]]; then
    trading_core_broker_capability="true"
    trading_core_broker_egress="true"
    trading_core_runtime_profile="trading-core-remote"
  fi
  trading_core_image_digest="$(oci_archive_digest "$archive_dir/trading-core.tar")"
  trading_core_service_inventory_hash="$(printf '%s' 'trading-core,market-data' | sha256sum | awk '{print $1}')"
  trading_core_secret_reference_hash="$(printf '%s' "$trading_core_encryption_secret|$trading_core_server_tls_secret|$market_data_trading_core_client_tls_secret" | sha256sum | awk '{print $1}')"
  trading_core_network_policy_hash="$(sha256sum k8s/network-policy-trading-core.yaml | awk '{print $1}')"
  sed -e "s|localhost/stock-trader/trading-core:latest|$trading_core_image|" \
    -e "s|__TRADING_CORE_DATA_DIR__|$trading_core_dir|" \
    -e "s|__TRADING_CORE_SERVER_TLS_SECRET__|$trading_core_server_tls_secret|" \
    -e "s|__TRADING_CORE_CLIENT_TLS_SECRET__|$trading_core_client_tls_secret|" \
    -e "s|__MARKET_DATA_TRADING_CORE_CLIENT_TLS_SECRET__|$market_data_trading_core_client_tls_secret|" \
    -e "s|__TRADING_CORE_ENCRYPTION_SECRET__|$trading_core_encryption_secret|" \
    -e "s|__TRADING_CORE_ENCRYPTION_GENERATION__|$trading_core_encryption_generation|" \
    -e "s|__TRADING_CORE_MODE__|$trading_core_mode|" \
    -e "s|__TRADING_CORE_BROKER_CAPABILITY_ENABLED__|$trading_core_broker_capability|" \
    -e "s|__TRADING_CORE_BROKER_EGRESS_ENABLED__|$trading_core_broker_egress|" \
    -e "s|__TRADING_CORE_BROKER_EGRESS_LABEL__|$(if [[ "$trading_core_broker_egress" == "true" ]]; then printf enabled; else printf disabled; fi)|" \
    -e "s|trading-core-production|$trading_core_runtime_profile|" \
    -e "s|__TRADING_CORE_IMAGE_DIGEST__|$trading_core_image_digest|" \
    -e "s|__TRADING_CORE_SERVICE_INVENTORY_HASH__|$trading_core_service_inventory_hash|" \
    -e "s|__TRADING_CORE_SECRET_REFERENCE_HASH__|$trading_core_secret_reference_hash|" \
    -e "s|__TRADING_CORE_NETWORK_POLICY_HASH__|$trading_core_network_policy_hash|" \
    k8s/deployment-trading-core.yaml | sudo k3s kubectl apply -f -
  sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-trading-core --timeout=300s
fi

if $deploy_api; then
  api_runtime_profile="api-local"
  api_has_broker_egress="true"
  if [[ "$trading_core_mode" == "Remote" ]]; then
    api_runtime_profile="api-remote"
    api_has_broker_egress="false"
  fi
  api_image_digest="$(oci_archive_digest "$archive_dir/api.tar")"
  api_service_inventory_hash="$(printf '%s' 'api,desktop,market-data,trading-core' | sha256sum | awk '{print $1}')"
  api_secret_reference_hash="$(printf '%s' "$trading_core_client_tls_secret|$edge_cutover_server_tls_secret|$cutover_coordinator_client_tls_secret" | sha256sum | awk '{print $1}')"
  api_network_policy_hash="$(sha256sum k8s/network-policy-api.yaml | awk '{print $1}')"
  rendered_api="$archive_dir/deployment-api.yaml"
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
      -e "s|__EDGE_TRANSITION_CONTROL_ENABLED__|true|" \
      -e "s|__EDGE_CUTOVER_SERVER_TLS_SECRET__|$edge_cutover_server_tls_secret|" \
      -e "s|__CUTOVER_COORDINATOR_CLIENT_TLS_SECRET__|$cutover_coordinator_client_tls_secret|" \
      -e "s|__API_RUNTIME_PROFILE__|$api_runtime_profile|" \
      -e "s|__API_IMAGE_DIGEST__|$api_image_digest|" \
      -e "s|__API_SERVICE_INVENTORY_HASH__|$api_service_inventory_hash|" \
      -e "s|__API_SECRET_REFERENCE_HASH__|$api_secret_reference_hash|" \
      -e "s|__API_NETWORK_POLICY_HASH__|$api_network_policy_hash|" \
      -e "s|__API_HAS_BROKER_EGRESS__|$api_has_broker_egress|" \
      -e "s|__API_BROKER_EGRESS_LABEL__|$(if [[ "$api_has_broker_egress" == "true" ]]; then printf enabled; else printf disabled; fi)|" \
    > "$rendered_api"
  if [[ "$trading_core_mode" == "Remote" ]]; then
    awk '
      skipping && /^        - name:/ { skipping=0 }
      /^        - name: ALPACA__/ { skipping=1; next }
      !skipping { print }
    ' "$rendered_api" > "$rendered_api.remote"
    mv "$rendered_api.remote" "$rendered_api"
  fi
  sudo k3s kubectl apply -f "$rendered_api"
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
