#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
namespace="${STOCKTRADER_NAMESPACE:-stocktrader}"
data_dir="${STOCKTRADER_TRADING_CORE_DIR:?Set STOCKTRADER_TRADING_CORE_DIR}"
new_generation="${STOCKTRADER_TRADING_CORE_ENCRYPTION_GENERATION:?Set a new encryption generation}"

if [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; then
  echo "STOCKTRADER_TRADING_CORE_DIR must be a safe absolute path below the filesystem root." >&2
  exit 1
fi
data_dir="$(realpath -e -- "$data_dir")"
if [[ "$data_dir" == "/" ]]; then
  echo "Resolved Trading Core directory cannot be the filesystem root." >&2
  exit 1
fi
if [[ ! "$new_generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]] || [[ "$new_generation" == "legacy" ]]; then
  echo "The new encryption generation must be 1-14 lowercase letters, digits, or hyphens, and cannot be legacy." >&2
  exit 1
fi

db_path="$data_dir/trading-core.db"
backup_dir="$data_dir/backups"
current_generation="$(sudo k3s kubectl -n "$namespace" get configmap \
  stocktrader-trading-core-encryption-active -o jsonpath='{.data.generation}' 2>/dev/null || true)"
current_generation="${current_generation:-legacy}"
if [[ "$current_generation" == "$new_generation" ]]; then
  echo "Encryption generation $new_generation is already active." >&2
  exit 1
fi
if [[ "$current_generation" == "legacy" ]]; then
  old_secret="stocktrader-trading-core-encryption"
else
  old_secret="stocktrader-trading-core-encryption-$current_generation"
fi
new_secret="stocktrader-trading-core-encryption-$new_generation"
job_name="stocktrader-trading-core-key-$new_generation"

sudo test -f "$db_path" || { echo "Trading Core database is missing." >&2; exit 1; }
mode="$(sudo sqlite3 "$db_path" "SELECT mode FROM authority WHERE singleton=1;")"
if [[ "$mode" == "Remote" ]]; then
  echo "Encryption rotation is prohibited while Trading Core is Remote." >&2
  exit 1
fi
sudo k3s kubectl -n "$namespace" get secret "$old_secret" >/dev/null
if sudo k3s kubectl -n "$namespace" get secret "$new_secret" >/dev/null 2>&1; then
  echo "Refusing to replace preserved encryption generation $new_generation." >&2
  exit 1
fi

secret_dir="$(mktemp -d /tmp/stocktrader-trading-core-encryption.XXXXXX)"
cleanup_secret() {
  rm -f -- "$secret_dir/encryption-key"
  rmdir "$secret_dir" 2>/dev/null || true
}
trap cleanup_secret EXIT
umask 077
openssl rand -base64 32 > "$secret_dir/encryption-key"
sudo k3s kubectl -n "$namespace" create secret generic "$new_secret" \
  --from-file=encryption-key="$secret_dir/encryption-key"
cleanup_secret
trap - EXIT

image="$(sudo k3s kubectl -n "$namespace" get deployment stocktrader-trading-core \
  -o jsonpath='{.spec.template.spec.containers[?(@.name=="trading-core")].image}')"
[[ -n "$image" ]] || { echo "Trading Core deployment image is missing." >&2; exit 1; }

sudo k3s kubectl -n "$namespace" scale deployment stocktrader-api stocktrader-trading-core --replicas=0
sudo k3s kubectl -n "$namespace" wait --for=delete pod \
  -l 'app in (stocktrader-api,stocktrader-trading-core)' --timeout=180s || true
sudo install -d -o 1654 -g 1654 -m 0750 "$backup_dir"
backup_path="$backup_dir/trading-core-before-key-$new_generation-$(date -u +%Y%m%dT%H%M%SZ).db"
sudo sqlite3 "$db_path" ".backup '$backup_path'"
sudo sqlite3 "$backup_path" "PRAGMA quick_check;" | grep -qx ok

database_may_have_changed=false
rotation_complete=false
recover() {
  if [[ "$rotation_complete" == "true" ]]; then return; fi
  sudo k3s kubectl -n "$namespace" delete job "$job_name" --ignore-not-found >/dev/null 2>&1 || true
  if [[ "$database_may_have_changed" == "true" ]]; then
    sudo install -o 1654 -g 1654 -m 0640 "$backup_path" "$db_path"
    sudo rm -f -- "$db_path-wal" "$db_path-shm"
  fi
  sudo k3s kubectl -n "$namespace" create configmap stocktrader-trading-core-encryption-active \
    --from-literal=generation="$current_generation" --dry-run=client -o yaml \
    | sudo k3s kubectl apply -f - >/dev/null
  patch="{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"trading-core\",\"env\":[{\"name\":\"STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY\",\"valueFrom\":{\"secretKeyRef\":{\"name\":\"$old_secret\",\"key\":\"encryption-key\"}}},{\"name\":\"STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY_GENERATION\",\"value\":\"$current_generation\"}]}]}}}}"
  sudo k3s kubectl -n "$namespace" patch deployment stocktrader-trading-core --type=strategic -p "$patch" >/dev/null || true
  sudo k3s kubectl -n "$namespace" scale deployment stocktrader-trading-core --replicas=1 >/dev/null || true
  sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-trading-core --timeout=180s || true
  sudo k3s kubectl -n "$namespace" scale deployment stocktrader-api --replicas=1 >/dev/null || true
}
trap recover ERR INT TERM

database_may_have_changed=true
sed -e "s|__TRADING_CORE_IMAGE__|$image|" \
  -e "s|__TRADING_CORE_DATA_DIR__|$data_dir|" \
  -e "s|__OLD_ENCRYPTION_SECRET__|$old_secret|" \
  -e "s|__OLD_ENCRYPTION_GENERATION__|$current_generation|" \
  -e "s|__NEW_ENCRYPTION_SECRET__|$new_secret|" \
  -e "s|__NEW_ENCRYPTION_GENERATION__|$new_generation|" \
  k8s/job-trading-core-encryption-rotation.yaml | sudo k3s kubectl apply -f -
sudo k3s kubectl -n "$namespace" wait --for=condition=complete "job/$job_name" --timeout=300s

sudo k3s kubectl -n "$namespace" create configmap stocktrader-trading-core-encryption-active \
  --from-literal=generation="$new_generation" --dry-run=client -o yaml \
  | sudo k3s kubectl apply -f -
patch="{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"trading-core\",\"env\":[{\"name\":\"STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY\",\"valueFrom\":{\"secretKeyRef\":{\"name\":\"$new_secret\",\"key\":\"encryption-key\"}}},{\"name\":\"STOCKTRADER_TRADING_CORE_ENCRYPTION_KEY_GENERATION\",\"value\":\"$new_generation\"}]}]}}}}"
sudo k3s kubectl -n "$namespace" patch deployment stocktrader-trading-core --type=strategic -p "$patch"
sudo k3s kubectl -n "$namespace" scale deployment stocktrader-trading-core --replicas=1
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-trading-core --timeout=180s
sudo k3s kubectl -n "$namespace" scale deployment stocktrader-api --replicas=1
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-api --timeout=300s
rotation_complete=true
trap - ERR INT TERM
sudo k3s kubectl -n "$namespace" delete job "$job_name" --ignore-not-found >/dev/null
echo "Trading Core encryption generation $new_generation is active."
echo "Rollback artifact: $backup_path with preserved generation $current_generation."
