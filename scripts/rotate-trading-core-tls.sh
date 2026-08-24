#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
namespace="${STOCKTRADER_NAMESPACE:-stocktrader}"
valid_days="${STOCKTRADER_TRADING_CORE_TLS_DAYS:-90}"
generation="${STOCKTRADER_TRADING_CORE_TLS_GENERATION:-$(date -u +%Y%m%d%H%M%S)}"
if [[ ! "$valid_days" =~ ^[0-9]+$ ]] || (( valid_days < 7 || valid_days > 397 )); then
  echo "STOCKTRADER_TRADING_CORE_TLS_DAYS must be between 7 and 397." >&2; exit 1
fi
if [[ ! "$generation" =~ ^[a-z0-9][a-z0-9-]{0,13}$ ]]; then
  echo "STOCKTRADER_TRADING_CORE_TLS_GENERATION must be 1-14 lowercase letters, digits, or hyphens." >&2; exit 1
fi
server_secret="stocktrader-trading-core-server-tls-$generation"
client_secret="stocktrader-trading-core-client-tls-$generation"
sudo k3s kubectl apply -f k8s/namespace.yaml
tls_dir="$(mktemp -d /tmp/stocktrader-trading-core-tls.XXXXXX)"
cleanup() {
  rm -f -- "$tls_dir/ca.key" "$tls_dir/ca.crt" "$tls_dir/ca.srl" \
    "$tls_dir/server.key" "$tls_dir/server.csr" "$tls_dir/server.crt" \
    "$tls_dir/client.key" "$tls_dir/client.csr" "$tls_dir/client.crt" \
    "$tls_dir/server.ext" "$tls_dir/client.ext"
  rmdir "$tls_dir" 2>/dev/null || true
}
trap cleanup EXIT
umask 077
openssl genrsa -out "$tls_dir/ca.key" 3072 >/dev/null 2>&1
openssl req -x509 -new -sha256 -days 397 -key "$tls_dir/ca.key" \
  -out "$tls_dir/ca.crt" -subj "/CN=StockTrader Trading Core Internal CA" >/dev/null 2>&1
openssl genrsa -out "$tls_dir/server.key" 2048 >/dev/null 2>&1
openssl req -new -sha256 -key "$tls_dir/server.key" -out "$tls_dir/server.csr" \
  -subj "/CN=stocktrader-trading-core" >/dev/null 2>&1
printf '%s\n' 'basicConstraints=critical,CA:FALSE' \
  'keyUsage=critical,digitalSignature,keyEncipherment' 'extendedKeyUsage=serverAuth' \
  'subjectAltName=DNS:stocktrader-trading-core,DNS:stocktrader-trading-core.stocktrader,DNS:stocktrader-trading-core.stocktrader.svc' \
  > "$tls_dir/server.ext"
openssl x509 -req -sha256 -days "$valid_days" -in "$tls_dir/server.csr" \
  -CA "$tls_dir/ca.crt" -CAkey "$tls_dir/ca.key" -CAcreateserial \
  -out "$tls_dir/server.crt" -extfile "$tls_dir/server.ext" >/dev/null 2>&1
openssl genrsa -out "$tls_dir/client.key" 2048 >/dev/null 2>&1
openssl req -new -sha256 -key "$tls_dir/client.key" -out "$tls_dir/client.csr" \
  -subj "/CN=stocktrader-api" >/dev/null 2>&1
printf '%s\n' 'basicConstraints=critical,CA:FALSE' 'keyUsage=critical,digitalSignature' \
  'extendedKeyUsage=clientAuth' > "$tls_dir/client.ext"
openssl x509 -req -sha256 -days "$valid_days" -in "$tls_dir/client.csr" \
  -CA "$tls_dir/ca.crt" -CAkey "$tls_dir/ca.key" -CAserial "$tls_dir/ca.srl" \
  -out "$tls_dir/client.crt" -extfile "$tls_dir/client.ext" >/dev/null 2>&1
sudo k3s kubectl -n "$namespace" create secret generic "$server_secret" \
  --from-file=tls.crt="$tls_dir/server.crt" --from-file=tls.key="$tls_dir/server.key" \
  --from-file=ca.crt="$tls_dir/ca.crt" --dry-run=client -o yaml | sudo k3s kubectl apply -f -
sudo k3s kubectl -n "$namespace" create secret generic "$client_secret" \
  --from-file=tls.crt="$tls_dir/client.crt" --from-file=tls.key="$tls_dir/client.key" \
  --from-file=ca.crt="$tls_dir/ca.crt" --dry-run=client -o yaml | sudo k3s kubectl apply -f -
sudo k3s kubectl -n "$namespace" create configmap stocktrader-trading-core-tls-active \
  --from-literal=generation="$generation" --dry-run=client -o yaml | sudo k3s kubectl apply -f -
echo "Trading Core TLS generation $generation created for $namespace; redeploy service and API together."
