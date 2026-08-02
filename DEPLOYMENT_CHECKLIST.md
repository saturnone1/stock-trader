# Stock Trader Desktop App - Deployment Checklist

**상태**: Phase 2 완료 → Phase 3 배포 준비 ✅

## 📋 배포 단계

### Step 1️⃣: 이미지 빌드
```bash
cd ~/projects/saturnone1/stock-trader
./scripts/build-k3s.sh
```

**확인 사항**:
```bash
sudo k3s ctr images ls | grep stock-trader
# 출력 예:
# - stock-trader/api:latest
# - stock-trader/desktop:latest
```

---

### Step 2️⃣: K3s 배포
```bash
# 네임스페이스 생성
kubectl create namespace stocktrader 2>/dev/null || true

# 매니페스트 배포
kubectl apply -f ~/projects/saturnone1/stock-trader/k8s/deployment-api.yaml
kubectl apply -f ~/projects/saturnone1/stock-trader/k8s/deployment-desktop.yaml

# 배포 상태 확인
kubectl get pods -n stocktrader
kubectl get svc -n stocktrader
```

**성공 조건**:
- `stocktrader-api` pod: Running (2/2 ready)
- `stocktrader-desktop` pod: Running (1/1 ready)
- 모든 pod CrashLoopBackOff 없음

---

### Step 3️⃣: 서비스 확인
```bash
# 로컬 테스트 (Port Forward)
kubectl port-forward svc/stocktrader-api 3000:3000 -n stocktrader &
kubectl port-forward svc/stocktrader-desktop 8000:8000 -n stocktrader &

# 엔드포인트 테스트
curl http://localhost:3000/api/health
curl http://localhost:8000

# Port-forward 중지
kill <PORT_FORWARD_PID>
```

**성공 조건**:
- API: HTTP 200 (health check)
- Desktop: HTTP 200 + HTML 응답

---

### Step 4️⃣: Traefik 라우팅 확인
```bash
# Ingress 확인
kubectl get ingress -n stocktrader

# 호스트명 해석 (local network)
# stock.taewon → API
# stock-desktop.taewon → Desktop UI

# Host 헤더로 직접 테스트
curl -H "Host: stock.taewon" http://192.0.2.10/api/health
curl -H "Host: stock-desktop.taewon" http://192.0.2.10
```

---

### Step 5️⃣: 기능 테스트

#### API 엔드포인트 점검
```bash
# 1. Health check
curl -H "Host: stock.taewon" http://192.0.2.10/api/health

# 2. Account info
curl http://192.0.2.10:3000/api/account

# 3. Patterns
curl http://192.0.2.10:3000/api/patterns

# 4. Optimization jobs
curl http://192.0.2.10:3000/api/optimization/jobs
```

#### Desktop UI 점검
1. 브라우저: `http://stock-desktop.taewon`
2. Dashboard 탭 → Account info 표시 확인
3. Optimization 탭 → Job list 표시 확인
4. Pattern Builder 탭 → Pattern list 표시 확인
5. Backtest 탭 → Form 표시 확인

---

## ⚙️ 트러블슈팅

### Pod가 CrashLoopBackOff 상태
```bash
# 로그 확인
kubectl logs -n stocktrader stocktrader-api
kubectl logs -n stocktrader stocktrader-desktop

# 매니페스트 재검토
kubectl describe pod -n stocktrader stocktrader-api
```

### API가 응답하지 않음
1. API pod 상태 확인: `kubectl get pod -n stocktrader -o wide`
2. 서비스 엔드포인트: `kubectl get endpoints -n stocktrader`
3. 포트 포워딩으로 직접 테스트

### Desktop UI가 API에 연결 못함
1. 환경변수 확인: `kubectl get deployment stocktrader-desktop -n stocktrader -o yaml | grep VITE_API_URL`
2. 기본값: `http://stocktrader-api:3000` (K3s 내부 네트워크)
3. 로컬 테스트 시: `.env.development`에서 `VITE_API_URL=http://localhost:3000`

---

## 📊 모니터링

### 실시간 모니터링
```bash
# 모든 리소스 감시
kubectl get all -n stocktrader -w

# Pod 로그 스트리밍
kubectl logs -f -n stocktrader stocktrader-api
kubectl logs -f -n stocktrader stocktrader-desktop
```

### 리소스 사용량
```bash
kubectl top nodes
kubectl top pods -n stocktrader
```

---

## 🔄 업데이트 및 재배포

### 이미지 업데이트 후 재배포
```bash
# 1. 새 이미지 빌드
./scripts/build-k3s.sh

# 2. 배포 업데이트 (롤링 업데이트)
kubectl rollout restart deployment/stocktrader-api -n stocktrader
kubectl rollout restart deployment/stocktrader-desktop -n stocktrader

# 3. 상태 확인
kubectl rollout status deployment/stocktrader-api -n stocktrader
kubectl rollout status deployment/stocktrader-desktop -n stocktrader
```

### 이전 버전으로 롤백
```bash
kubectl rollout undo deployment/stocktrader-api -n stocktrader
kubectl rollout undo deployment/stocktrader-desktop -n stocktrader
```

---

## 📝 다음 단계 (Phase 3-4)

- [x] 기존 React 앱 제거 (`ClientApp/` 폴더 삭제)
- [ ] 패턴빌더 고급 UI (3-pane, 드래그-드롭)
- [ ] WebSocket 실시간 업데이트 (선택사항)
- [ ] Tauri 데스크톱 바이너리 빌드 (.deb, .exe, .dmg)
- [ ] CI/CD 파이프라인 구성

---

## ✅ 체크리스트

- [ ] 이미지 빌드 완료
- [ ] K3s 배포 완료
- [ ] 모든 pod Running 상태
- [ ] API 헬스 체크 성공
- [ ] Desktop UI 접속 성공
- [ ] Dashboard 데이터 표시 성공
- [ ] 다른 탭 기능 확인
- [ ] Traefik 라우팅 작동
