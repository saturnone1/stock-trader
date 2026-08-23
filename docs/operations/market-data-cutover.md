# Market Data service cutover and recovery evidence

- Accepted: 2026-08-24 KST (cluster timestamps are UTC on 2026-08-23)
- Implementation head: `6587b65`
- Branch: `codex/msa-market-data`
- Namespace: `stocktrader`
- Authority after the drill: `MarketDataTransport.Mode=Remote`
- Active TLS generation after rollback: `md45a7e44`

## Accepted topology

`stocktrader-market-data` runs as an independent one-replica F# Deployment with its own
ServiceAccount, image, ClusterIP Service, NetworkPolicy, provider Secret, generation-scoped mTLS
Secrets, resource limits, probes, metrics, and host-backed SQLite directory. The API mounts only a
client identity and reaches the service contract; it does not mount the Market Data database. One
replica is deliberate while SQLite is canonical.

The accepted final images were:

- API: `localhost/stock-trader/api:architecture-2bab68b96c9b`
- Market Data: `localhost/stock-trader/market-data:architecture-6587b6562445`

Both Deployments were `1/1 Ready` with zero restarts after the final rollout. The desktop returned
HTTP 200 at `http://192.168.0.11:7681/`.

## Data and consumer evidence

Shadow backfill compared every populated legacy daily series exactly: AAPL, AMZN, GOOGL, MSFT, SPY,
and TSLA each matched 275 bars. Direct authenticated Alpaca checks returned TQQQ history (16 bars),
latest evidence (6 bars), intraday history (373 bars), and a current price. Requests without a
client certificate and with a wrong workload secret both returned 401.

In Remote mode:

- TQQQ preview returned 15 bars, 2 entries, no warning, and 4.678534647638664116521995% return;
- TQQQ backtest returned 22 trades with explicit Alpaca/SplitsAndDividends evidence;
- one-combination optimization tested one result, returned 22 trades and
  4.39632999535357232947932463% return;
- the legacy `OhlcvBars` count did not change during Remote preview/backtest/provider ingestion;
- API startup logged that in-process Alpaca streaming was disabled;
- an Alpaca subscription for SQQQ and TQQQ reported `streamingConnected=true`, while metrics showed
  `stocktrader_market_data_stream_connected 1` and zero service failures.

The final authoritative store contained 2,903 bars at revision 12. Repeating the same SQQQ request
left both values unchanged, proving production idempotency. Unit conformance also covers concurrent
duplicate requests, monotonic corrections, provider-separated identity, out-of-range rejection,
invalid numeric provider fields, and evidence-hash recomputation.

## Failure, load, and recovery drills

- Pod loss: deleting the Market Data Pod recreated it with the same persisted bars and revision.
- Service outage: scaling Market Data to zero made API readiness return 503 while `/api/health/live`
  remained 200. After two probe intervals the API restart count stayed zero; restoring the service
  returned the API to Ready without intervention.
- Provider outage: a temporary unreachable Alpaca data endpoint made a new SQQQ preview return 502
  in 0.15 seconds. API/service health remained available, no local/Yahoo fallback occurred, and the
  legacy bar count stayed unchanged. The official deployment restored the endpoint and the same
  request returned 200.
- Load: 24 preview requests at concurrency 6 all returned 200. The observed API/Market Data samples
  were 34m CPU/136MiB and 27m CPU/55MiB respectively.
- Backup/restore: `marketdata-final-22897cf-20260823T143000Z.db` passed `PRAGMA quick_check`; the
  official restore script stopped both owners, created a pre-restore copy, restored 2,613 bars at
  revision 11, and returned both Deployments to Ready.
- TLS: generation `md923e93a` was created and used by both service and API, then both were rolled
  back to preserved generation `md45a7e44`; Remote health passed in both directions.

## Local rollback and recutover

The official deploy path created a pre-projection compatibility backup, read every authoritative
Alpaca series, wrote the compatibility store, and compared every bar by normalized identity and
financial value. The resulting 2,613 legacy bars were AAPL 275, AMZN 275, GOOGL 275, MSFT 275,
SPY 433, TQQQ 805, and TSLA 275. Local TQQQ preview reproduced the Remote 15-bar, 2-entry, and exact
return result. The API was then redeployed to Remote. Subsequent SQQQ ingestion increased only the
Market Data store (to 2,903 bars); legacy remained at 2,613.

## Defects found during the full service drill

1. API liveness and readiness used the same downstream-dependent endpoint, causing an API restart
   when Market Data was absent. Liveness now measures only API process survival.
2. The protected service UID correctly blocked ordinary access to backup files, but the restore
   script also used ordinary `realpath/sqlite3`. Its validation now crosses that boundary with sudo
   while directory permissions remain restrictive.
3. SQLite decimal scale and an inclusive final timestamp changed storage representation without
   changing financial value. Writes now preserve contract decimal text; rollback validates the
   remote transport hash first, then compares a padded read window by exact UTC identity and numeric
   OHLCV/VWAP value.
4. Alpaca reports stream authentication success as `authenticated`; parsing `authorized` rejected
   valid credentials. The service now validates the structured JSON success message.

## Recovery commands

Use only the repository scripts and generation-scoped Secrets:

```bash
STOCKTRADER_MARKET_DATA_DIR=/home/saturnone1/stock-trader-data/market-data \
  scripts/restore-market-data-backup.sh /home/saturnone1/stock-trader-data/market-data/backups/<backup>.db

STOCKTRADER_MARKET_DATA_TLS_GENERATION=<generation> \
  scripts/rotate-market-data-tls.sh

STOCKTRADER_DEPLOY_SCOPE=api \
STOCKTRADER_DATA_DIR=/home/saturnone1/stock-trader-data \
STOCKTRADER_MARKET_DATA_MODE=Local \
  scripts/deploy-k3s.sh <release-tag>
```

The Local deployment command automatically creates a compatibility backup and projects verified
Market Data evidence before changing authority. If projection differs by count, identity, timestamp,
or any financial field, deployment stops and the running Remote authority is retained.
