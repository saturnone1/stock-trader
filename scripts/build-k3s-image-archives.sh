#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

release_tag="${1:-$(git rev-parse --short=12 HEAD)}"
output_dir="${2:?Usage: scripts/build-k3s-image-archives.sh [release-tag] OUTPUT_DIR [scope]}"
scope="${3:-all}"
mkdir -p "$output_dir"

build_archive() {
  local dockerfile="$1"
  local image_name="$2"
  local archive_name="$3"
  local target_stage="${4:-}"
  local image="localhost/stock-trader/${image_name}:architecture-${release_tag}"
  local target_args=()
  [[ -n "$target_stage" ]] && target_args=(--target "$target_stage")
  buildah bud --layers "${target_args[@]}" -f "$dockerfile" -t "$image" .
  buildah push "$image" "oci-archive:$output_dir/$archive_name:$image"
}

image_file_hash() {
  local image="$1"
  local path="$2"
  local container
  container="$(buildah from "$image")"
  local value
  value="$(buildah run "$container" -- sha256sum "$path" | awk '{print $1}')"
  buildah rm "$container" >/dev/null
  printf '%s' "$value"
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

case "$scope" in
  all)
    build_archive Dockerfile.api api-local api.tar local
    build_archive Dockerfile.desktop desktop desktop.tar
    build_archive Dockerfile.optimization-worker optimization-worker worker.tar
    build_archive Dockerfile.market-data market-data market-data.tar
    build_archive Dockerfile.ml-training ml-training ml-training.tar
    build_archive Dockerfile.trading-core-shadow trading-core-shadow trading-core.tar
    ;;
  trading-core-stage5)
    build_archive Dockerfile.api api-local api-local.tar local
    build_archive Dockerfile.api api-remote api-remote.tar remote
    build_archive Dockerfile.market-data market-data market-data.tar
    build_archive Dockerfile.trading-core trading-core trading-core.tar
    build_archive Dockerfile.trading-core-shadow trading-core-shadow trading-core-shadow.tar
    build_archive Dockerfile.trading-core-acceptance trading-core-acceptance trading-core-acceptance.tar
    build_archive Dockerfile.trading-core-broker-emulator trading-core-broker-emulator trading-core-broker-emulator.tar
    build_archive Dockerfile.trading-core-acceptance-driver trading-core-acceptance-driver trading-core-acceptance-driver.tar
    build_archive Dockerfile.trading-core-cutover-coordinator trading-core-cutover-coordinator trading-core-cutover-coordinator.tar
    build_archive Dockerfile.edge-rollback-importer edge-rollback-importer edge-rollback-importer.tar
    ;;
  *)
    echo "scope must be all or trading-core-stage5" >&2
    exit 1
    ;;
esac

if [[ "$scope" == "trading-core-stage5" ]]; then
  remote_image="localhost/stock-trader/trading-core:architecture-${release_tag}"
  acceptance_image="localhost/stock-trader/trading-core-acceptance:architecture-${release_tag}"
  service_contracts_hash="$(image_file_hash "$remote_image" /app/StockTrader.ServiceContracts.dll)"
  engine_hash="$(image_file_hash "$remote_image" /app/StockTrader.Engine.dll)"
  trading_core_hash="$(image_file_hash "$remote_image" /app/StockTrader.TradingCore.dll)"
  runtime_hash="$(image_file_hash "$remote_image" /app/StockTrader.TradingCore.Runtime.dll)"
  [[ "$service_contracts_hash" == "$(image_file_hash "$acceptance_image" /app/StockTrader.ServiceContracts.dll)" \
    && "$engine_hash" == "$(image_file_hash "$acceptance_image" /app/StockTrader.Engine.dll)" \
    && "$trading_core_hash" == "$(image_file_hash "$acceptance_image" /app/StockTrader.TradingCore.dll)" \
    && "$runtime_hash" == "$(image_file_hash "$acceptance_image" /app/StockTrader.TradingCore.Runtime.dll)" ]] || {
      echo "Production and acceptance financial runtime assemblies differ." >&2
      exit 1
    }
  {
    printf 'REPOSITORY_COMMIT=%s\n' "$(git rev-parse HEAD)"
    printf 'BUILD_ID=%s\n' "$release_tag"
    printf 'SERVICE_CONTRACTS_HASH=%s\n' "$service_contracts_hash"
    printf 'ENGINE_HASH=%s\n' "$engine_hash"
    printf 'TRADING_CORE_HASH=%s\n' "$trading_core_hash"
    printf 'RUNTIME_HASH=%s\n' "$runtime_hash"
    printf 'EDGE_IMAGE_DIGEST=%s\n' "$(oci_archive_digest "$output_dir/api-remote.tar")"
    printf 'TRADING_CORE_IMAGE_DIGEST=%s\n' "$(oci_archive_digest "$output_dir/trading-core.tar")"
    printf 'MARKET_DATA_IMAGE_DIGEST=%s\n' "$(oci_archive_digest "$output_dir/market-data.tar")"
    printf 'ACCEPTANCE_CORE_IMAGE_DIGEST=%s\n' "$(oci_archive_digest "$output_dir/trading-core-acceptance.tar")"
    printf 'BROKER_EMULATOR_IMAGE_DIGEST=%s\n' "$(oci_archive_digest "$output_dir/trading-core-broker-emulator.tar")"
    printf 'DRIVER_IMAGE_DIGEST=%s\n' "$(oci_archive_digest "$output_dir/trading-core-acceptance-driver.tar")"
  } > "$output_dir/stage5-metadata.env"
fi

if [[ -f "$output_dir/stage5-metadata.env" ]]; then
  (cd "$output_dir" && sha256sum -- *.tar stage5-metadata.env > SHA256SUMS)
else
  (cd "$output_dir" && sha256sum -- *.tar > SHA256SUMS)
fi
