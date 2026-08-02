#!/bin/bash
set -e

echo "🚀 Deploying to K3s..."

# Create namespace
echo "📦 Creating namespace..."
kubectl create namespace stocktrader 2>/dev/null || echo "Namespace already exists"

# Load images
echo "📥 Loading images to K3s..."
sudo k3s ctr images import <(docker save stock-trader/api:latest) 2>/dev/null || true
sudo k3s ctr images import <(docker save stock-trader/desktop:latest) 2>/dev/null || true

# Deploy
echo "🚀 Deploying manifests..."
kubectl apply -f k8s/deployment-api.yaml
kubectl apply -f k8s/deployment-desktop.yaml

echo "⏳ Waiting for deployments..."
kubectl rollout status deployment/stocktrader-api -n stocktrader
kubectl rollout status deployment/stocktrader-desktop -n stocktrader

echo "✅ Deployment complete!"
echo ""
echo "📍 Service endpoints:"
kubectl get svc -n stocktrader
echo ""
echo "🔗 Traefik routes:"
kubectl get ingress -n stocktrader
