#!/bin/bash
set -e

echo "🐳 Building Stock Trader images for K3s..."

cd /home/stocktrader/projects/saturnone1/stock-trader

# API
echo "📦 Building API image with buildah..."
sudo buildah build-using-dockerfile -f Dockerfile.api -t localhost/stock-trader/api:latest .
sudo buildah tag localhost/stock-trader/api:latest stock-trader/api:latest

# Desktop
echo "📦 Building Desktop image with buildah..."
sudo buildah build-using-dockerfile -f Dockerfile.desktop-prod -t localhost/stock-trader/desktop:latest .
sudo buildah tag localhost/stock-trader/desktop:latest stock-trader/desktop:latest

# Load to K3s
echo "📥 Loading images to K3s containerd..."
sudo buildah push localhost/stock-trader/api:latest oci-archive:/tmp/api.tar
sudo buildah push localhost/stock-trader/desktop:latest oci-archive:/tmp/desktop.tar

sudo k3s ctr images import /tmp/api.tar
sudo k3s ctr images import /tmp/desktop.tar

echo "✅ Images built and loaded!"
sudo k3s ctr images ls | grep stock-trader
