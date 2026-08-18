# Public repository configuration sanitization

- Removed workstation-specific paths, hostnames, and contact identifiers from tracked defaults.
- K3s deployments now require `STOCKTRADER_HOST`; the deployment script validates and injects it
  into the ingress manifest alongside `STOCKTRADER_DATA_DIR`.
- Added a public security policy and an architecture regression test that rejects private paths,
  local hostnames, and private IPv4 addresses in public deployment artifacts.
- Audited all reachable Git history with Gitleaks before publishing this change; no secrets were
  detected.
