# Security policy

## Reporting a vulnerability

Do not open a public issue for vulnerabilities or exposed credentials. Use GitHub's private
vulnerability reporting or a private security advisory for this repository.

## Credential handling

- Never commit broker keys, account credentials, encryption keys, cookies, databases, or `.env`
  files.
- Supply local secrets through .NET user secrets or environment variables.
- Supply production secrets through Kubernetes Secrets. Start from
  `k8s/secret.example.yaml`, but never commit the populated `k8s/secret.yaml` file.
- Treat any credential pasted into an issue, pull request, log, or commit as compromised and rotate
  it immediately. Removing it from the latest commit is not sufficient.

## Public deployment configuration

Production paths and hostnames are deployment inputs rather than repository defaults. The K3s
deployment requires both values explicitly:

```bash
sudo env \
  STOCKTRADER_DATA_DIR=/srv/stocktrader \
  STOCKTRADER_HOST=stocktrader.example.com \
  bash scripts/deploy-k3s.sh
```

GitHub secret scanning and push protection should remain enabled. CI also scans every change with
Gitleaks.
