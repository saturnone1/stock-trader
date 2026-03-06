#!/bin/bash
# StockTrader k3s 배포 스크립트
# Usage: bash deploy-k3s.sh

set -e

REMOTE="minipc"
IMAGE_NAME="stocktrader"
IMAGE_TAG="latest"
TAR_FILE="stocktrader-image.tar"

echo "=== 1. Building Docker image for linux/amd64 ==="
docker build --platform linux/amd64 -t ${IMAGE_NAME}:${IMAGE_TAG} .

echo "=== 2. Saving image to tar ==="
docker save ${IMAGE_NAME}:${IMAGE_TAG} -o ${TAR_FILE}

echo "=== 3. Transferring image to mini PC ==="
scp ${TAR_FILE} ${REMOTE}:~/${TAR_FILE}

echo "=== 4. Importing image into k3s containerd ==="
ssh ${REMOTE} "sudo k3s ctr images import ~/${TAR_FILE} && rm ~/${TAR_FILE}"

echo "=== 5. Applying k8s manifests ==="
scp -r k8s/ ${REMOTE}:~/k8s/
ssh ${REMOTE} "kubectl apply -f ~/k8s/namespace.yaml"
ssh ${REMOTE} "kubectl apply -f ~/k8s/secret.yaml"
ssh ${REMOTE} "kubectl apply -f ~/k8s/pvc.yaml"
ssh ${REMOTE} "kubectl apply -f ~/k8s/deployment.yaml"
ssh ${REMOTE} "kubectl apply -f ~/k8s/service.yaml"
ssh ${REMOTE} "kubectl apply -f ~/k8s/ingress.yaml"

echo "=== 6. Waiting for pod to start ==="
ssh ${REMOTE} "kubectl -n stocktrader rollout status deployment/stocktrader --timeout=120s"

echo "=== 7. Status ==="
ssh ${REMOTE} "kubectl -n stocktrader get all"

echo ""
echo "=== Deploy complete! ==="
echo "Access: http://192.0.2.10"

# Cleanup local tar
rm -f ${TAR_FILE}
