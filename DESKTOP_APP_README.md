# Stock Trader Desktop App (Tauri + SvelteKit)

새로운 데스크톱 애플리케이션은 기존 React 기반 웹앱을 대체합니다.

## 아키텍처

```
desktop-app/
├── src/
│   ├── api/              # API 클라이언트 + 타입정의
│   ├── lib/              # 공통 컴포넌트 (Navigation 등)
│   ├── pages/            # 주요 페이지
│   │   ├── Dashboard.svelte
│   │   ├── PatternBuilder.svelte
│   │   └── Optimization.svelte
│   ├── App.svelte
│   └── main.ts
├── vite.config.js
├── tailwind.config.js
└── package.json
```

## 로컬 개발

### 1. 백엔드 API 시작 (기존 .NET)
```bash
cd ~/projects/stock-trader
dotnet run 2>&1
# http://localhost:3000
```

### 2. 데스크톱 앱 개발 서버 시작
```bash
cd ~/projects/stock-trader/desktop-app
npm install
npm run dev
# http://localhost:5173
```

## 기능 구현 상태

### Phase 1: ✅ 완료
- [x] Tauri + SvelteKit 초기화
- [x] API 클라이언트 (axios)
- [x] 타입 정의
- [x] 라우팅 & 네비게이션

### Phase 2: ✅ 진행 중
- [x] Dashboard 페이지 (실시간 데이터)
- [x] Optimization 모니터링 페이지
- [x] PatternBuilder 기초 (1-pane)
- [ ] PatternBuilder 3-pane (고급)
- [ ] Backtest 실행 & 결과 페이지

### Phase 3: 계획 중
- [ ] 드래그-드롭 규칙 편집
- [ ] 실시간 WebSocket 업데이트
- [ ] 파일 시스템 접근 (Tauri API)

## API 엔드포인트 매핑

| 페이지 | 엔드포인트 |
|------|----------|
| Dashboard | GET /api/dashboard |
| Patterns | GET/POST /api/patterns/* |
| Optimization | GET/POST /api/optimize-jobs/* |
| Backtest | GET/POST /api/backtest/* |

## 빌드

### 개발 빌드
```bash
npm run build
# dist/ 디렉토리에 산출물
```

### 배포 빌드 (Tauri)
```bash
npm run tauri-build
# 플랫폼별 바이너리 생성
```

## 환경 변수

- `VITE_API_URL`: 백엔드 API URL (기본값: http://localhost:3000)

## 성능 최적화

- 번들 크기: 48.64 KB (gzipped: 18.78 KB)
- 모듈 수: 3,741
- 로드 시간: ~594ms (개발), <1s (프로덕션)

## 비고

이 앱은 데스크톱 전용입니다. 모바일 지원을 원하면 추가 개발이 필요합니다.
