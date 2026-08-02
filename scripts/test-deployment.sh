#!/bin/bash

echo "🧪 Testing deployment..."

# Test API
echo ""
echo "Testing API (localhost:3000)..."
curl -s http://localhost:3000/api/health || echo "❌ API not responding"

# Test Desktop
echo ""
echo "Testing Desktop (localhost:8000)..."
curl -s http://localhost:8000 > /dev/null && echo "✅ Desktop OK" || echo "❌ Desktop not responding"

# K3s services
if [ "$1" = "k3s" ]; then
  echo ""
  echo "Testing K3s services..."
  
  echo "API service:"
  kubectl get svc -n stocktrader stocktrader-api
  
  echo "Desktop service:"
  kubectl get svc -n stocktrader stocktrader-desktop
  
  echo "Pods:"
  kubectl get pods -n stocktrader
fi
