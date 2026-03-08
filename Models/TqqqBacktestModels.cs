using StockTrader.Models.Enums;

namespace StockTrader.Models;

/// <summary>
/// fmkorea "200일 티큐 변형매매법" 전략 파라미터.
/// Python BasicParams 구조체를 C# 속성으로 1:1 번역.
/// </summary>
public class TqqqStrategyParams
{
    // ── RSI / 재진입 ─────────────────────────────────────────────────────────
    public int     RsiLen          { get; set; } = 14;
    public decimal RsiReentryThr   { get; set; } = 20.0m;   // 최적화: 43→20 (빠른 재진입, 10~30 무감대)

    // ── 변동성 락 ────────────────────────────────────────────────────────────
    public decimal VolThreshold    { get; set; } = 0.059m;
    public int     VolLen          { get; set; } = 20;

    // ── TQQQ 200일선 히스테리시스 ────────────────────────────────────────────
    public decimal Dist200Enter    { get; set; } = 98.00m;    // % (최적화: 101→98, 더 빠른 진입)
    public decimal Dist200Exit     { get; set; } = 99.00m;    // %

    // ── 기울기 부스트 ────────────────────────────────────────────────────────
    public bool    UseSlopeBoost   { get; set; } = true;
    public int     SlopeLen        { get; set; } = 17;      // 최적화: 45→17 (빠른 기울기 감지)
    public decimal SlopeThr        { get; set; } = 0.0050m;   // %/day (최적화: 0.11→0.005)
    public decimal DistCap         { get; set; } = 95.0m;     // 200일선 대비 % (최적화: 98.8→95, 깊은 풀백에서만 부스트)
    public decimal VolCap          { get; set; } = 0.07m;     // vol 상한 (무감대 0.055~0.10)

    // ── 과열 단계 히스테리시스 ───────────────────────────────────────────────
    public bool    UseOverheatSplit { get; set; } = true;

    /// <summary>stage 1: 진입 기준 (TQQQ/TQQQ_MA200 × 100)</summary>
    public decimal Overheat1Enter  { get; set; } = 139.0m;
    public decimal Overheat1Exit   { get; set; } = 132.0m;

    /// <summary>stage 2</summary>
    public decimal Overheat2Enter  { get; set; } = 146.0m;
    public decimal Overheat2Exit   { get; set; } = 138.0m;

    /// <summary>stage 3</summary>
    public decimal Overheat3Enter  { get; set; } = 149.0m;
    public decimal Overheat3Exit   { get; set; } = 140.0m;

    /// <summary>stage 4 — exit 기준이 매우 낮아 사실상 익절 구간</summary>
    public decimal Overheat4Enter  { get; set; } = 151.0m;
    public decimal Overheat4Exit   { get; set; } = 118.0m;

    // ── 원금보호 손절 ────────────────────────────────────────────────────────
    public bool    UsePrincipalStop     { get; set; } = true;
    /// <summary>풀진입가 대비 이 비율 이하로 하락하면 전량 청산 (예: 0.941 = -5.9%)</summary>
    public decimal PrincipalStopPct     { get; set; } = 0.941m;

    // ── SPY 필터 ─────────────────────────────────────────────────────────────
    public bool    UseSpyFilter         { get; set; } = false;  // 최적화: SPY 필터 끔 (수익률 +86%p, MDD 동일)
    /// <summary>SPY/SPY_MA200 × 100 이상이면 강세 진입</summary>
    public decimal SpyEnter             { get; set; } = 100.25m;
    /// <summary>SPY/SPY_MA200 × 100 이하이면 약세 전환</summary>
    public decimal SpyExit              { get; set; } = 97.75m;
    public int     SpyConfirmDays       { get; set; } = 1;
    /// <summary>SPY 약세 시 비중 상한 (0 = 전부 청산)</summary>
    public decimal SpyBearCap           { get; set; } = 0.0m;

    // ── TP10 사이클 ──────────────────────────────────────────────────────────
    public bool    UseTp10              { get; set; } = false;  // 최적화: TP10 끔 (감량 없이 홀딩)
    /// <summary>100% 진입 후 이 수익률 달성 시 95%로 감량</summary>
    public decimal Tp10Trigger          { get; set; } = 0.10m;
    /// <summary>TP10 감량 목표 비중</summary>
    public decimal Tp10Cap              { get; set; } = 0.95m;
}

/// <summary>
/// TQQQ 비중 백테스트 요청 모델.
/// </summary>
public class TqqqBacktestRequest
{
    public DateTime  From           { get; set; }
    public DateTime  To             { get; set; }
    public decimal   InitialCapital { get; set; } = 100_000m;
    public DataSource? DataSource   { get; set; }
    /// <summary>null이면 기본 파라미터(TqqqStrategyParams 기본값) 사용.</summary>
    public TqqqStrategyParams? Params { get; set; }
}

/// <summary>
/// TQQQ 비중 백테스트 결과 요약.
/// </summary>
public class TqqqBacktestResult
{
    public decimal TotalReturnPercent   { get; set; }
    public decimal Cagr                 { get; set; }
    public decimal MaxDrawdownPercent   { get; set; }
    public decimal SharpeRatio          { get; set; }
    public int     TotalDays            { get; set; }
    /// <summary>비중 > 0인 날 수.</summary>
    public int     InvestedDays         { get; set; }
    public List<TqqqDailyRecord> DailyRecords { get; set; } = new();
}

/// <summary>
/// 일별 비중/수익 기록.
/// </summary>
public class TqqqDailyRecord
{
    public DateTime Date              { get; set; }
    /// <summary>
    /// 비중 코드: 0=0%, 1=10%, 2=100%, 3=90%, 4=80%, 5=5%.
    /// 1000 이상이면 code/1000 비율 (중간 단계).
    /// </summary>
    public int      Code              { get; set; }
    /// <summary>실제 비중 (0 ~ 1).</summary>
    public decimal  Weight            { get; set; }
    public decimal  TqqqClose         { get; set; }
    /// <summary>당일 포트폴리오 수익률 = weight × TQQQ 수익률.</summary>
    public decimal  DailyReturn       { get; set; }
    public decimal  CumulativeReturn  { get; set; }
    public decimal  Equity            { get; set; }
}
