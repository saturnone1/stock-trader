#!/usr/bin/env bash
set -euo pipefail

if (( $# != 1 )); then
  echo "Usage: STOCKTRADER_TRADING_CORE_DIR=/absolute/path $0 <backup-file>" >&2
  exit 1
fi

data_dir="${STOCKTRADER_TRADING_CORE_DIR:?Set STOCKTRADER_TRADING_CORE_DIR}"
if [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; then
  echo "STOCKTRADER_TRADING_CORE_DIR must be a safe absolute path below root." >&2
  exit 1
fi
resolved_data="$(sudo realpath -e "$data_dir")"
resolved_backup="$(sudo realpath -e "$1")"
case "$resolved_backup" in
  "$resolved_data"/backups/*) ;;
  *) echo "Backup must be inside $resolved_data/backups." >&2; exit 1 ;;
esac

database="$resolved_data/trading-core.db"
if ! sudo test -f "$database"; then
  echo "Current Trading Core database does not exist: $database" >&2
  exit 1
fi
sudo sqlite3 "$resolved_backup" "PRAGMA quick_check;" | grep -qx ok
current_mode="$(sudo sqlite3 "$database" "SELECT mode FROM authority WHERE singleton=1;")"
backup_mode="$(sudo sqlite3 "$resolved_backup" "SELECT mode FROM authority WHERE singleton=1;")"
current_generation="$(sudo sqlite3 "$database" "SELECT generation FROM authority WHERE singleton=1;")"
backup_generation="$(sudo sqlite3 "$resolved_backup" "SELECT generation FROM authority WHERE singleton=1;")"
if [[ "$current_mode" == "Remote" || "$backup_mode" == "Remote" ]]; then
  echo "Remote authority restore requires a separately reconciled single-writer runbook." >&2
  exit 1
fi
if [[ "$backup_mode" != "$current_mode" || "$backup_generation" != "$current_generation" ]]; then
  echo "Backup authority mode/generation must match the current non-Remote authority." >&2
  exit 1
fi

namespace="${STOCKTRADER_NAMESPACE:-stocktrader}"
restore_id="$(date -u +%Y%m%dT%H%M%SZ)"
restore_point="$resolved_data/backups/trading-core-before-restore-$restore_id.db"
staging="$resolved_data/trading-core-restore-$restore_id.db"
scaled_down=false
cleanup() {
  sudo rm -f -- "$staging"
  if [[ "$scaled_down" == true ]]; then
    sudo k3s kubectl -n "$namespace" scale deployment \
      stocktrader-trading-core stocktrader-api --replicas=1 >/dev/null || true
  fi
}
trap cleanup EXIT

sudo k3s kubectl -n "$namespace" scale deployment \
  stocktrader-api stocktrader-trading-core --replicas=0
scaled_down=true
sudo k3s kubectl -n "$namespace" wait --for=delete pod \
  -l 'app in (stocktrader-api,stocktrader-trading-core)' --timeout=180s

sudo sqlite3 "$database" ".backup '$restore_point'"
sudo sqlite3 "$restore_point" "PRAGMA quick_check;" | grep -qx ok
sudo sqlite3 "$resolved_backup" ".backup '$staging'"
sudo sqlite3 "$staging" "PRAGMA quick_check;" | grep -qx ok
sudo chown 1654:1654 "$staging"
sudo chmod 0640 "$staging"
sudo rm -f -- "$database-wal" "$database-shm"
sudo mv -- "$staging" "$database"

sudo k3s kubectl -n "$namespace" scale deployment stocktrader-trading-core --replicas=1
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-trading-core --timeout=300s
sudo k3s kubectl -n "$namespace" scale deployment stocktrader-api --replicas=1
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-api --timeout=300s
scaled_down=false
echo "Trading Core restored from $resolved_backup; pre-restore copy: $restore_point"
