# Stock Trader 배포 가이드 (Tauri + SvelteKit)

## 아키텍처 변경

### Before (React + Blazor Server)
```
Client Browser
    ↓
React UI (wwwroot)
    ↓
Blazor Server + REST API
    ↓
Database
```

### After (SvelteKit Desktop + .NET API)
```
Desktop App (Tauri/Svelte)
    ↓
REST API (3000)
    ↓
Database
```

**주요 변경:**
- ✅ UI와 API 분리 (마이크로서비스 아키텍처)
- ✅ 데스크톱 우선 인터페이스
- ✅ Blazor Server 제거 (순수 API 서버)
- ✅ 모바일 지원 제거 (데스크톱 only)

---

## 로컬 개발

### 1. 백엔드 API (3000)
```bash
cd ~/projects/saturnone1/stock-trader
dotnet run 2>&1
```

### 2. 데스크톱 앱 개발 (5173)
```bash
cd ~/projects/saturnone1/stock-trader/desktop-app
npm run dev
```

**접속:**
- API: http://localhost:3000
- Desktop: http://localhost:5173

---

## Docker 배포

### 로컬 Docker 빌드

#### 1️⃣ API 컨테이너
```bash
cd ~/projects/saturnone1/stock-trader
docker build -f Dockerfile.api -t stock-trader/api:latest .
```

#### 2️⃣ Desktop 컨테이너
```bash
docker build -f Dockerfile.desktop-prod -t stock-trader/desktop:latest .
```

#### 3️⃣ 함께 실행
```bash
docker-compose -f docker-compose.prod.yml up -d
```

**포트 매핑:**
- API: http://localhost:3000
- Desktop: http://localhost:8000

---

## K3s 배포

### Prerequisites
```bash
# K3s 클러스터 확인
kubectl cluster-info
kubectl get nodes

# stocktrader 네임스페이스 생성
kubectl create namespace stocktrader
```

### 1️⃣ 이미지를 K3s로 로드

```bash
# containerd 이미지 로드
sudo k3s ctr images import <(docker save stock-trader/api:latest)
sudo k3s ctr images import <(docker save stock-trader/desktop:latest)

# 또는 현지 이미지 사용 (개발 중)
sudo k3s ctr images ls | grep stock-trader
```

### 2️⃣ API 배포
```bash
kubectl apply -f k8s/deployment-api.yaml
```

확인:
```bash
kubectl get pods -n stocktrader
kubectl get svc -n stocktrader
kubectl logs -n stocktrader -l app=stocktrader-api
```

### 3️⃣ Desktop 배포
```bash
kubectl apply -f k8s/deployment-desktop.yaml
```

### 4️⃣ Traefik 라우팅 확인
```bash
# 로컬 DNS 설정 (호스트 파일)
# 192.0.2.10 stock.taewon
# 192.0.2.10 stock-desktop.taewon

# 또는 Traefik 대시보드에서 확인
curl http://192.0.2.10:9000/dashboard/
```

---

## 배포 단계

### Step 1: Blazor 비활성화 (완료)
```bash
# Program.cs에서 이미 제거됨
# - AddRazorComponents() 제거
# - MapRazorComponents() 제거
# - Blazor 정적 파일 제거
```

### Step 2: Docker 이미지 빌드
```bash
# 프로덕션 이미지
./scripts/build-images.sh
```

### Step 3: K3s 배포
```bash
# K3s에 배포
./scripts/deploy-k3s.sh
```

### Step 4: 테스트
```bash
# 엔드포인트 테스트
curl http://localhost:3000/api/dashboard
curl http://localhost:8000/

# K3s 서비스 테스트
kubectl port-forward -n stocktrader svc/stocktrader-api 3000:3000
curl http://localhost:3000/api/dashboard
```

---

## 마이그레이션 체크리스트

- [x] API Server (Blazor UI 제거)
- [x] SvelteKit Desktop App 완성
- [x] Docker 설정 (API + Desktop)
- [x] K8s Ingress 설정
- [ ] 로컬 테스트
- [ ] K3s 배포 테스트
- [ ] 기존 React 앱 제거
- [ ] 문서 업데이트

---

## 트러블슈팅

### API 응답 안 함
```bash
# 컨테이너 로그 확인
docker logs stocktrader-api
kubectl logs -n stocktrader <pod-name>

# 엔드포인트 체크
curl -v http://localhost:3000/api/health
```

### Desktop 앱 로딩 안 됨
```bash
# 브라우저 콘솔에서 API 에러 확인
# CORS 설정 확인 (Program.cs)
# API URL 환경변수 확인
```

### K3s 배포 실패
```bash
# 이미지 로드 확인
sudo k3s ctr images ls | grep stock-trader

# 파드 상태 확인
kubectl describe pod -n stocktrader <pod-name>

# 서비스 엔드포인트 확인
kubectl get endpoints -n stocktrader
```

---

## 환경 변수

### API (.NET)
| 변수 | 값 | 설명 |
|-----|-----|------|
| ASPNETCORE_ENVIRONMENT | Production | 환경 |
| ASPNETCORE_URLS | http://+:3000 | 바인드 주소 |

### Desktop (Node.js)
| 변수 | 값 | 설명 |
|-----|-----|------|
| VITE_API_URL | http://api:3000 | 백엔드 URL |
| NODE_ENV | production | 환경 |

---

## 성능 최적화

### API
- [x] Minimal APIs (빠른 라우팅)
- [x] In-memory caching
- [x] Async/await 최적화

### Desktop
- [x] SvelteKit (번들 최소화)
- [x] Tailwind CSS (트리 쉐이킹)
- [x] 동적 import (code splitting)

---

## 모니터링

### K3s 메트릭 수집
```bash
# Prometheus (선택)
kubectl apply -f https://raw.githubusercontent.com/prometheus-community/helm-charts/main/...

# 간단한 모니터링
watch kubectl top nodes -n stocktrader
```

### 로그 수집
```bash
# Serilog + 파일 로깅 (API)
# 로그 경로: /app/logs/

# Desktop 브라우저 콘솔
# DevTools에서 확인
```

---

## 롤백 절차

```bash
# 이전 이미지로 되돌리기
kubectl set image deployment/stocktrader-api \
  api=stock-trader/api:v1.0 -n stocktrader

kubectl rollout status deployment/stocktrader-api -n stocktrader
```

---

## 다음 단계

1. [x] 로컬 테스트 (docker-compose)
2. [x] K3s 배포 테스트
3. [ ] 외부 접근 테스트 (모바일, 타 PC)
4. [x] 기존 ClientApp 제거
5. [ ] CI/CD 파이프라인 구성
