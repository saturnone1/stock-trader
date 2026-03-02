# StockTrader - Claude Code 프로젝트 가이드

## 프로젝트 개요
C# .NET 10.0 Blazor Server 주식 자동매매 프로그램. MudBlazor 9.0 다크 테마 UI, SQLite DB, Alpaca Markets 브로커 연동.

## 핵심 규칙

### 사용자 선호
- **절대 yes/no 확인 묻지 말 것** — 모든 진행은 yes로 간주하고 바로 실행
- **플랜모드 사용 금지** — 승인 요구하지 말고 바로 구현
- 자율적으로 판단해서 알아서 진행

### Windows Bash 주의사항
- `dotnet`, `curl`, `git` 등의 stdout이 Bash 도구에서 안 보이는 경우 빈번
- **항상 `> /tmp/out.txt 2>&1` 리다이렉트 후 Read 도구로 확인**
- 빌드: `dotnet build StockTrader.csproj --no-restore > /tmp/build_out.txt 2>&1`
- 테스트: `dotnet test trader.sln --no-restore > /tmp/test_out.txt 2>&1`
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
- 포트: 5239 (appsettings.json Kestrel)
- Symbols/Patterns는 반드시 JSON 배열 (문자열 아님)
- DataSource: null=기본설정, 0=Alpaca, 2=Yahoo
- **Alpaca 데이터 기준으로 최적화** (Yahoo와 결과 완전히 다름)

## 아키텍처

```
Configuration/    설정 클래스 (PatternSettings, TradingSettings 등)
Models/           도메인 모델 + Enums
Data/             Repositories (EF Core + SQLite)
Services/         비즈니스 로직
  Patterns/       13개 패턴 디텍터 (IPatternDetector)
  Backtest/       백테스트 엔진
  Indicators/     기술적 지표 서비스
  ML/             머신러닝 (K-Means, FastTree)
BackgroundServices/  7개 백그라운드 서비스
Components/
  Pages/          11개 페이지 (.razor)
  Shared/         8개 공유 컴포넌트
  Layout/         NavMenu, MainLayout
Extensions/       DI 등록 (ServiceCollectionExtensions.cs)
```

### 핵심 파이프라인
```
MarketData → PatternScanner → SignalService(기대값필터) → RiskCheck → OrderService(Bracket)
```

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
| `Services/Backtest/BacktestService.cs` | 백테스트 엔진 (WalkForward, MonteCarlo) |

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
| 에이전트 | 주 담당 영역 | 부 담당 |
|----------|-------------|---------|
| senior-backend-engineer | Services/, BackgroundServices/, Extensions/, Configuration/, Models/ | Program.cs, Data/ |
| frontend-ux-improver | Components/Pages/, Components/Shared/, Components/Layout/ | wwwroot/ |
| qa-bug-hunter | 읽기 전용 (코드 수정 불가, 리포트만 작성) | — |
| trading-algorithm-researcher | Services/Patterns/, Services/Indicators/ | Models/PatternType.cs |
| algo-backtest-optimizer | 읽기 전용 (백테스트 API 호출만, 코드 수정 불가) | — |
| stock-program-architect | 읽기 전용 (설계/기획만, 코드 수정 불가) | — |

### 코드 수정 규칙
1. 수정 전 **반드시 파일 Read** (현재 코드 파악)
2. **Edit 도구로 최소 변경** (관련 없는 코드 건드리지 말 것)
3. 수정 후 **빌드 확인**: `dotnet build StockTrader.csproj > /tmp/build_out.txt 2>&1`
4. Razor 파일 수정 시 주의:
   - `Position` → `StockTrader.Models.Position` (MudBlazor 충돌)
   - `@변수한글` → `@(변수)한글` (파싱 오류)
   - `PanelClass` → MudBlazor 9.x에서 `Class` 사용
5. 네임스페이스 충돌 항상 체크 (ApexCharts, MudBlazor)

### 빌드-테스트 의무
- 모든 코드 수정 에이전트는 작업 완료 후 빌드 확인 필수
- stdout 안 보이면 `/tmp/` 리다이렉트 후 Read로 확인
- 테스트는 오케스트레이터가 최종 병합 후 실행

### 앱 재시작 프로토콜
- 앱 실행/재시작 시 **반드시 기존 프로세스 먼저 종료**
- `powershell.exe -Command "Get-NetTCPConnection -LocalPort 5239 | Select -Expand OwningProcess -First 1"` → PID 확인
- `powershell.exe -Command "Stop-Process -Id {PID} -Force"` → 종료
- 포트 해제 확인 후 재시작

## 절대 하지 말 것
- MeanReversionChannel exit profiles 수정 (30%+ 성능 하락)
- Regime filter 제거 (Breakout/TrendPullback 성능 크게 하락)
- 분봉 패턴에 일봉 파라미터 적용 (손실 발생)
- ApexCharts 타입을 MudBlazor와 네임스페이스 충돌 — 항상 풀 경로 사용
- 에이전트가 지정된 파일 외의 파일을 수정하는 것
