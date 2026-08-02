#!/bin/bash
set -e

echo "🐳 Building Stock Trader Docker images..."

# API
echo "📦 Building API image..."
docker build -f Dockerfile.api -t stock-trader/api:latest .
docker tag stock-trader/api:latest stock-trader/api:$(date +%Y%m%d)

# Desktop
echo "📦 Building Desktop image..."
docker build -f Dockerfile.desktop-prod -t stock-trader/desktop:latest .
docker tag stock-trader/desktop:latest stock-trader/desktop:$(date +%Y%m%d)

echo "✅ Images built successfully!"
docker images | grep stock-trader
