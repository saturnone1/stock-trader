#!/usr/bin/env bash
set -euo pipefail

if (( $# != 1 )); then
  echo "Usage: STOCKTRADER_MARKET_DATA_DIR=/absolute/path $0 <backup-file>" >&2
  exit 1
fi

data_dir="${STOCKTRADER_MARKET_DATA_DIR:?Set STOCKTRADER_MARKET_DATA_DIR}"
if [[ ! "$data_dir" =~ ^/[A-Za-z0-9._/-]+$ ]] || [[ "$data_dir" == "/" ]]; then
  echo "STOCKTRADER_MARKET_DATA_DIR must be a safe absolute path below root." >&2
  exit 1
fi
resolved_data="$(realpath -e "$data_dir")"
resolved_backup="$(realpath -e "$1")"
case "$resolved_backup" in
  "$resolved_data"/backups/*) ;;
  *) echo "Backup must be inside $resolved_data/backups." >&2; exit 1 ;;
esac

sqlite3 "$resolved_backup" "PRAGMA quick_check;" | grep -qx ok
namespace="${STOCKTRADER_NAMESPACE:-stocktrader}"
sudo k3s kubectl -n "$namespace" scale deployment stocktrader-api stocktrader-market-data --replicas=0
sudo k3s kubectl -n "$namespace" wait --for=delete pod \
  -l 'app in (stocktrader-api,stocktrader-market-data)' --timeout=180s || true

restore_point="$resolved_data/backups/marketdata-before-restore-$(date -u +%Y%m%dT%H%M%SZ).db"
if sudo test -f "$resolved_data/marketdata.db"; then
  sudo sqlite3 "$resolved_data/marketdata.db" ".backup '$restore_point'"
fi
sudo sqlite3 "$resolved_backup" ".backup '$resolved_data/marketdata.db'"
sudo sqlite3 "$resolved_data/marketdata.db" "PRAGMA quick_check;" | grep -qx ok

sudo k3s kubectl -n "$namespace" scale deployment stocktrader-market-data stocktrader-api --replicas=1
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-market-data --timeout=300s
sudo k3s kubectl -n "$namespace" rollout status deployment/stocktrader-api --timeout=300s
echo "Market Data restored from $resolved_backup; pre-restore copy: $restore_point"
