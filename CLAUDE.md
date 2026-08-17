# StockTrader - Claude Code 프로젝트 가이드

## 프로젝트 개요
C# .NET 10.0 주식 자동매매 프로그램. 프론트엔드: SvelteKit 데스크톱 앱 (`desktop-app/`), 백엔드: REST API 서버. SQLite DB, Alpaca Markets 브로커 연동.

## 핵심 규칙

### 프로젝트 상태 (최우선)
- **`stock-trader`는 현재 운영·개선 중인 활성 프로젝트다.**
- 신규 구조 작업은 루트 `AGENTS.md`와 `docs/architecture/`의 모듈형 모놀리스 원칙을 따른다.
- **`desktop-app/`의 Svelte UI가 유일한 신규 기능 대상이다.** `Components/`의 Blazor UI는 제거 전까지 유지보수만 한다.
- 사용자에게 보이는 변경은 로컬 빌드에서 끝내지 않고 K3s 배포와 상태 확인까지 수행한다.
- 전략 결과를 바꾸는 수정은 반드시 특성화 테스트와 변경 이유를 함께 남긴다.

### Fleet / Tasks 운용 규칙
- 이 프로젝트처럼 **백엔드, 패턴 엔진, `desktop-app`, 백테스트, K3s 배포**가 나뉜 구조에서는 단일 직렬 처리 금지
- **Fleet(병렬 에이전트 탐색)** 는 아래에 사용:
  1. 패턴/UI 반영 경로 조사
  2. API/DB 저장 경로 조사
  3. K3s 배포 경로/이미지 갱신 경로 조사
  4. 로컬/전역 정책 충돌 확인
- **Tasks(실행 단위)** 는 아래에 사용:
  1. `dotnet build StockTrader.csproj 2>&1`
  2. `dotnet test trader.sln 2>&1`
  3. `cd desktop-app && npm run build`
  4. `./scripts/build-k3s.sh`
  5. `kubectl rollout restart ...`
  6. `kubectl get pods`, `kubectl logs ...`
- 기본 순서:
  1. **Fleet로 원인/영향범위 병렬 파악**
  2. 필요한 수정 수행
  3. **Tasks로 빌드/테스트/배포/로그 확인**
- UI가 안 보이거나 반영 여부가 의심될 때는 **배포 누락 여부를 fleet 조사 항목에 반드시 포함**

### 사용자 선호
- **절대 yes/no 확인 묻지 말 것** — 모든 진행은 yes로 간주하고 바로 실행
- **플랜모드 사용 금지** — 승인 요구하지 말고 바로 구현
- 자율적으로 판단해서 알아서 진행
- **단, 기능 추가/제거/수정은 사용자에게 먼저 물어볼 것** — 코드 구현은 자율적으로, 기능 결정은 사용자 확인 필수

### Windows Bash 주의사항
- `dotnet` 등 일부 CLI는 stderr로 출력 → Bash 도구에서 stdout만 캡처되어 안 보임
- **해결: 명령어 끝에 `2>&1` 추가** (stderr를 stdout으로 합침)
- 빌드: `dotnet build StockTrader.csproj 2>&1`
- 테스트: `dotnet test trader.sln 2>&1`
- `/tmp/` 임시파일 리다이렉트 사용 금지 (파일 쓰레기 누적됨)
- 프로젝트/솔루션 파일 2개 이상 → 반드시 파일명 지정 (StockTrader.csproj 또는 trader.sln)

### 빌드-테스트-커밋 워크플로우
1. 코드 변경 후 **반드시 `dotnet build StockTrader.csproj`** 확인
2. 빌드 성공 시 **`dotnet test trader.sln`** 실행하여 147+ 테스트 통과 확인
3. 테스트 통과 후에만 커밋
4. **파라미터 변경 시 관련 테스트 기대값도 반드시 동기화** (BreakoutDetectorTests 등)

### 패턴 상태 관리
- 새 패턴 추가 시 `PatternMetadata.cs`에 `PatternStatus` 반드시 지정
  - `Verified`: 백테스트 최적화 완료
  - `Untuned`: 데이터 부족 등으로 미최적화
  - `Poor`: 승률/수익률 기준 미달
- `PatternSettings.cs`에 Config 클래스 추가, `appsettings.json`에 기본값 등록

### 백테스트 API
- Endpoint: `POST http://localhost:5239/api/backtest`
- 최적화 Endpoint: `POST http://localhost:5239/api/backtest/optimize`
- 포트: 5239 (appsettings.json Kestrel)
- Symbols/Patterns는 반드시 JSON 배열 (문자열 아님)
- DataSource: null=기본설정, 0=Alpaca, 2=Yahoo
- **Alpaca 데이터 기준으로 최적화** (Yahoo와 결과 완전히 다름)

### 파라미터 최적화 엔진 (`BacktestService.RunOptimizationAsync`)
- **2단계 전략**: Stage 1 (60% 예산 랜덤 샘플링) → Stage 2 (40% 이웃 탐색)
- **IS/OOS 분할**: 기본 75% In-Sample / 25% Out-of-Sample (과적합 방지)
- **메모리 보호**: 조합 5만개 초과 시 랜덤 인덱스 샘플링 (전체 카르테시안 곱 생성 안 함)
- 최적화 가능 파라미터: 숫자형 14개 + 카테고리형 6개 + 룰 파라미터/필드 오버라이드
- 관련 파일: `Api/OptimizeEndpoints.cs` (모델/엔드포인트), `Services/Backtest/BacktestService.cs` (엔진)

## 아키텍처

```
desktop-app/      SvelteKit 데스크톱 프론트엔드
  src/pages/      Dashboard, Optimization, PatternBuilder, Backtest
  src/lib/        공유 컴포넌트
Api/              REST API 엔드포인트 (Minimal API)
Configuration/    설정 클래스 (PatternSettings, TradingSettings 등)
Models/           도메인 모델 + Enums
Data/             Repositories (EF Core + SQLite)
Services/         비즈니스 로직
  Patterns/       13개 패턴 디텍터 (IPatternDetector)
  Backtest/       백테스트 엔진 + 파라미터 최적화
  Indicators/     기술적 지표 서비스
  ML/             머신러닝 (K-Means, FastTree)
BackgroundServices/  7개 백그라운드 서비스
Components/       Blazor 페이지 (레거시, 점진적 이관 중)
  Pages/          11개 페이지 (.razor)
  Shared/         8개 공유 컴포넌트
  Layout/         NavMenu, MainLayout
Extensions/       DI 등록 (ServiceCollectionExtensions.cs)
```

### 핵심 파이프라인
```
MarketData → PatternScanner → SignalService(6단계필터) → OrderService(Bracket)
```

### 시그널 평가 정책 (SignalService 6단계 필터)
추천("자동매매 추천") 생성 조건 = **실제 자동매매 실행 가능한 시그널만 추천**
1. **신뢰도** — `Confidence >= MinConfidence(0.3)` (TradingSettings)
2. **가격 유효성** — `StopLoss < Entry < Target`
3. **기대값** — `Expectancy > MinExpectancy` (샘플 10건 미만 시 우회)
4. **리스크** — `CanOpenPositionAsync` 통과 (포지션 한도, 섹터 한도)
5. **포지션 사이징** — `1/MaxTotalPositions` 캡 적용
6. **주문 수량** — `ShareQuantity > 0` (0주면 매수 불가)

**스타트업 소급 적용**: Program.cs에서 기존 DB의 저품질 시그널 비활성화 + 무효 추천 삭제
**파라미터 변경 시**: `appsettings.json` Trading.MinConfidence 수정 → 스타트업 정리 자동 적용

### 패턴 분류 (16개, PatternType enum)
- **일봉 검증됨(7)**: Breakout, TrendPullback, VolatilityExpansion, MomentumReversal, MeanReversionChannel, Rsi2Bollinger, Tqqq200Sma
- **미튜닝(3)**: GapUpPullback, VwapReversion, VolumeSpikeContinuation
- **성능불량(2)**: MultiTimeframeTrend(WR 18%), VolatilityBreakout(WR 34-45%)
- **특수/미구현(4)**: RsiMeanReversion, OpeningRangeBreakout, EarningsDrift, IndexRegimeFilter

### 최적화 현황 (Alpaca 2019-2025, 일봉 7패턴)
- 520 trades, 681% return, 17.4% MDD, 0.199 Sharpe, 53.8% WR

## 주요 파일 경로
| 파일 | 역할 |
|------|------|
| `Program.cs` | Entry point + Backtest API endpoint |
| `Extensions/ServiceCollectionExtensions.cs` | 전체 DI 등록 |
| `appsettings.json` | Alpaca, Trading, Patterns 설정 |
| `Configuration/PatternSettings.cs` | 패턴별 Config 클래스 |
| `Models/PatternMetadata.cs` | 패턴 메타데이터 + 상태 + UI 헬퍼 |
| `Services/Backtest/BacktestService.cs` | 백테스트 엔진 (WalkForward, MonteCarlo, 파라미터 최적화) |
| `Api/OptimizeEndpoints.cs` | 파라미터 최적화 요청/응답 모델 + 엔드포인트 |
| `Api/ApiEndpointExtensions.cs` | REST API 라우트 그룹 등록 (/api) |
| `desktop-app/src/pages/PatternBuilder.svelte` | 패턴 빌더 UI (Svelte) |

## 에이전트 공통 정책 (Agent Shared Policy)

### 최우선 규칙: 이전 변경사항 보존
- **기존 코드를 절대 덮어쓰지 말 것** — 이전 에이전트/세션의 변경사항 반드시 유지
- 파일 수정 전 **반드시 Read로 현재 상태 확인** → Edit으로 최소 범위만 수정
- Write(전체 덮어쓰기)는 새 파일 생성 시에만 사용, 기존 파일은 Edit 필수

### 파일 소유권 규칙 (병렬 작업 시)
- 오케스트레이터가 각 에이전트에 **수정 가능한 파일 목록을 명시적으로 지정**
- 지정되지 않은 파일 수정 금지 (읽기는 자유)
- 동일 파일을 두 에이전트가 동시에 수정하는 것은 절대 금지
- 의존성 있는 파일(예: 인터페이스+구현체)은 같은 에이전트에 배정

### 에이전트별 담당 영역 (기본값)

**코드 수정 에이전트 (`.claude/agents/`에 정의):**
| 에이전트 | 주 담당 영역 | 부 담당 |
|----------|-------------|---------|
| senior-backend-engineer | Services/Order/, Services/Signal/, Services/Risk/, Services/Account/, Services/ML/ | Services/Backtest/ |
| data-infra-engineer | Data/, BackgroundServices/, Extensions/, Configuration/, Models/ | Program.cs |
| notification-engineer | Services/Notification/, BackgroundServices/DailyReportService.cs | — |
| frontend-ux-improver | desktop-app/src/, Components/Pages/, Components/Shared/, Components/Layout/ | wwwroot/ |
| trading-algorithm-researcher | Services/Patterns/, Services/Indicators/ | Models/PatternType.cs |
| docker-ops | Dockerfile, docker-compose*.yml, .dockerignore, .env.example, .env | .gitignore (Docker 관련만) |

**검증/감사 에이전트 (읽기 전용, 코드 수정 불가):**
| 에이전트 | 역할 |
|----------|------|
| qa-bug-hunter | 코드 정적 분석, 버그 리포트 작성 |
| runtime-validator | 배포 후 런타임 기능 검증 (로그/데이터흐름/페이지 일관성) |
| security-auditor | 보안 감사 (인증/암호화/OWASP/자격증명/헤더) |
| algo-backtest-optimizer | 백테스트 API 호출, 파라미터 최적화 |
| stock-program-architect | 설계/기획, 아키텍처 리뷰 |

### 코드 수정 규칙
1. 수정 전 **반드시 파일 Read** (현재 코드 파악)
2. **Edit 도구로 최소 변경** (관련 없는 코드 건드리지 말 것)
3. 수정 후 **빌드 확인**: `dotnet build StockTrader.csproj 2>&1`
4. Razor 파일 수정 시 주의:
   - `Position` → `StockTrader.Models.Position` (MudBlazor 충돌)
   - `@변수한글` → `@(변수)한글` (파싱 오류)
   - `PanelClass` → MudBlazor 9.x에서 `Class` 사용
5. 네임스페이스 충돌 항상 체크 (ApexCharts, MudBlazor)

### 빌드-테스트 의무
- 모든 코드 수정 에이전트는 작업 완료 후 빌드 확인 필수
- stdout 안 보이면 `2>&1` 추가 (임시파일 사용 금지)
- 테스트는 오케스트레이터가 최종 병합 후 실행

### 앱 실행 프로토콜
- **반드시 `dotnet publish` → publish 폴더에서 exe 실행** (`dotnet build` exe 직접 실행 금지 — wwwroot 누락으로 UI 깨짐)
- 실행 순서:
  1. 기존 프로세스 종료: `powershell.exe -Command "Get-NetTCPConnection -LocalPort 5239 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }"`
  2. 빌드: `dotnet publish StockTrader.csproj -c Release -o ./publish 2>&1`
  3. 실행: `cd publish && start "" "StockTrader.exe"` (publish 폴더에서 실행해야 content root가 올바름)
- 접속: http://localhost:5239
- 종료: 콘솔 창 닫기 또는 Ctrl+C

### K3s 배포 (기본 운영 환경)
- **기본 배포 = K3s (buildah + containerd)**
- 이미지 빌드/로드: `./scripts/build-k3s.sh`
- 배포: `kubectl apply -f k8s/deployment-api.yaml && kubectl apply -f k8s/deployment-desktop.yaml`
- API 재시작: `kubectl rollout restart deployment/stocktrader-api -n stocktrader`
- Desktop 재시작: `kubectl rollout restart deployment/stocktrader-desktop -n stocktrader`
- 로그: `kubectl logs deployment/stocktrader-api -n stocktrader --tail=50`
- Pod 상태: `kubectl get pods -n stocktrader`
- `.env` 파일: K8s Secret으로 관리
- **DataProtection 키**: `/data/keys`에 영구 저장 (SecurityServiceExtensions.cs)

### 프론트엔드 빌드
- Desktop UI: `cd desktop-app && npm run build`
- 배포 이미지는 `Dockerfile.desktop-prod`에서 빌드됨

## 절대 하지 말 것
- MeanReversionChannel exit profiles 수정 (30%+ 성능 하락)
- Regime filter 제거 (Breakout/TrendPullback 성능 크게 하락)
- 분봉 패턴에 일봉 파라미터 적용 (손실 발생)
- ApexCharts 타입을 MudBlazor와 네임스페이스 충돌 — 항상 풀 경로 사용
- 에이전트가 지정된 파일 외의 파일을 수정하는 것
