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

(cd "$output_dir" && sha256sum -- *.tar > SHA256SUMS)
