#!/usr/bin/env bash
set -euo pipefail

if (( $# != 1 )); then
  echo "Usage: STOCKTRADER_ML_TRAINING_DIR=/absolute/path $0 <backup-file>" >&2; exit 1
fi
data_dir="${STOCKTRADER_ML_TRAINING_DIR:?Set STOCKTRADER_ML_TRAINING_DIR}"
if [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; then
  echo "STOCKTRADER_ML_TRAINING_DIR must be a safe absolute path below root." >&2; exit 1
fi
resolved_data="$(sudo realpath -e "$data_dir")"
resolved_backup="$(sudo realpath -e "$1")"
case "$resolved_backup" in
  "$resolved_data"/backups/*) ;;
  *) echo "Backup must be inside $resolved_data/backups." >&2; exit 1 ;;
esac
sudo sqlite3 "$resolved_backup" "PRAGMA quick_check;" | grep -qx ok
namespace="${STOCKTRADER_NAMESPACE:-stocktrader}"
sudo k3s kubectl -n "$namespace" scale deployment stocktrader-ml-training --replicas=0
sudo k3s kubectl -n "$namespace" wait --for=delete pod -l app=stocktrader-ml-training --timeout=180s || true
restore_point="$resolved_data/backups/jobs-before-restore-$(date -u +%Y%m%dT%H%M%SZ).db"
if sudo test -f "$resolved_data/jobs.db"; then
  sudo sqlite3 "$resolved_data/jobs.db" ".backup '$restore_point'"
fi
sudo sqlite3 "$resolved_backup" ".backup '$resolved_data/jobs.db'"
sudo sqlite3 "$resolved_data/jobs.db" "PRAGMA quick_check;" | grep -qx ok
sudo k3s kubectl -n "$namespace" scale deployment stocktrader-ml-training --replicas=1
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-ml-training --timeout=600s
echo "ML Training jobs restored from $resolved_backup; pre-restore copy: $restore_point"
