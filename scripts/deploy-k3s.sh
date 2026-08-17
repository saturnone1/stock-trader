#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

release_tag="${1:-$(git rev-parse --short=12 HEAD)}"
api_image="localhost/stock-trader/api:architecture-${release_tag}"
desktop_image="localhost/stock-trader/desktop:architecture-${release_tag}"
archive_dir="$(mktemp -d /tmp/stocktrader-deploy.XXXXXX)"

cleanup() {
  sudo rm -f -- "$archive_dir/api.tar" "$archive_dir/desktop.tar"
  rmdir "$archive_dir" 2>/dev/null || true
}
trap cleanup EXIT

sudo k3s kubectl apply -f k8s/namespace.yaml
if ! sudo k3s kubectl -n stocktrader get secret stocktrader-alpaca >/dev/null 2>&1; then
  echo "Missing Kubernetes secret stocktrader/stocktrader-alpaca." >&2
  echo "Create it from k8s/secret.example.yaml before deploying." >&2
  exit 1
fi

sudo buildah bud --layers -f Dockerfile.api -t "$api_image" .
sudo buildah bud --layers -f Dockerfile.desktop -t "$desktop_image" .

sudo buildah push "$api_image" "oci-archive:$archive_dir/api.tar:$api_image"
sudo buildah push "$desktop_image" "oci-archive:$archive_dir/desktop.tar:$desktop_image"
sudo k3s ctr images import "$archive_dir/api.tar"
sudo k3s ctr images import "$archive_dir/desktop.tar"

if sudo k3s kubectl -n stocktrader get deployment stocktrader-api >/dev/null 2>&1; then
  # A RollingUpdate deployment contains a server-defaulted rollingUpdate field.
  # Clear it atomically while switching strategy so Kubernetes accepts Recreate.
  sudo k3s kubectl -n stocktrader patch deployment stocktrader-api --type=merge \
    -p '{"spec":{"strategy":{"type":"Recreate","rollingUpdate":null}}}'
fi

sed "s|localhost/stock-trader/api:latest|$api_image|" k8s/deployment-api.yaml \
  | sudo k3s kubectl apply -f -
sed "s|localhost/stock-trader/desktop:latest|$desktop_image|" k8s/deployment-desktop.yaml \
  | sudo k3s kubectl apply -f -

sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-api --timeout=300s
sudo k3s kubectl -n stocktrader rollout status deployment/stocktrader-desktop --timeout=180s
sudo k3s kubectl -n stocktrader get deployment stocktrader-api stocktrader-desktop
sudo k3s kubectl -n stocktrader get pods -l 'app in (stocktrader-api,stocktrader-desktop)'
