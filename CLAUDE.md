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

## 절대 하지 말 것
- MeanReversionChannel exit profiles 수정 (30%+ 성능 하락)
- Regime filter 제거 (Breakout/TrendPullback 성능 크게 하락)
- 분봉 패턴에 일봉 파라미터 적용 (손실 발생)
- ApexCharts 타입을 MudBlazor와 네임스페이스 충돌 — 항상 풀 경로 사용
