#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
namespace="${STOCKTRADER_NAMESPACE:-stocktrader}"
generation="${STOCKTRADER_TRADING_CORE_AUTH_GENERATION:-$(date -u +%Y%m%d%H%M%S)}"

if [[ ! "$generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]] || [[ "$generation" == "legacy" ]]; then
  echo "STOCKTRADER_TRADING_CORE_AUTH_GENERATION must be 1-14 lowercase letters, digits, or hyphens, and cannot be legacy." >&2
  exit 1
fi

secret_name="stocktrader-trading-core-auth-$generation"
sudo k3s kubectl apply -f k8s/namespace.yaml
if sudo k3s kubectl -n "$namespace" get secret "$secret_name" >/dev/null 2>&1; then
  echo "Refusing to replace existing immutable authentication generation $generation." >&2
  exit 1
fi

secret_dir="$(mktemp -d /tmp/stocktrader-trading-core-auth.XXXXXX)"
cleanup() {
  rm -f -- "$secret_dir/shared-secret"
  rmdir "$secret_dir" 2>/dev/null || true
}
trap cleanup EXIT
umask 077

if [[ -n "${STOCKTRADER_TRADING_CORE_AUTH_SECRET:-}" ]]; then
  if (( ${#STOCKTRADER_TRADING_CORE_AUTH_SECRET} < 32 )); then
    echo "STOCKTRADER_TRADING_CORE_AUTH_SECRET must contain at least 32 characters." >&2
    exit 1
  fi
  printf '%s' "$STOCKTRADER_TRADING_CORE_AUTH_SECRET" > "$secret_dir/shared-secret"
else
  openssl rand -base64 48 > "$secret_dir/shared-secret"
fi

# Create, rather than apply, so a named generation can never be changed in place.
sudo k3s kubectl -n "$namespace" create secret generic "$secret_name" \
  --from-file=shared-secret="$secret_dir/shared-secret"
sudo k3s kubectl -n "$namespace" create configmap stocktrader-trading-core-auth-active \
  --from-literal=generation="$generation" --dry-run=client -o yaml \
  | sudo k3s kubectl apply -f -

echo "Trading Core authentication generation $generation created for $namespace."
echo "Redeploy Trading Core and API with the same generation; use legacy or a preserved generation for rollback."
