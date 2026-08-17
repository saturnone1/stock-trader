using StockTrader.Models.Enums;

namespace StockTrader.Models;

/// <summary>
/// 사용자 정의 패턴. 지표 조건을 조합하여 자신만의 매매 전략을 정의합니다.
/// </summary>
public class CustomPatternDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>진입 조건 목록 (JSON: List&lt;EntryRule&gt;)</summary>
    public string EntryRulesJson { get; set; } = "[]";

    /// <summary>진입 조건 결합 방식: "AND" 또는 "OR"</summary>
    public string EntryLogic { get; set; } = "AND";

    /// <summary>강세장(SPY > 200SMA) 필터 사용 여부</summary>
    public bool RequireBullRegime { get; set; }

    // ── 청산 설정 ──
    public decimal AtrStopMultiplier { get; set; } = 2.0m;
    public decimal AtrTargetMultiplier { get; set; } = 3.0m;
    public int MaxHoldingBars { get; set; } = 10;
    public decimal TrailingAtr { get; set; }
    public decimal PartialProfitR { get; set; }

    // ── 비중 단계 설정 ──

    /// <summary>비중 단계 사용 여부</summary>
    public bool UseWeightTiers { get; set; }

    /// <summary>비중 단계 목록 (JSON: List&lt;WeightTier&gt;). 위에서부터 순서대로 평가, 첫 매칭 적용.</summary>
    public string WeightTiersJson { get; set; } = "[]";

    /// <summary>어떤 비중 단계에도 매칭 안 될 때 기본 투자 비중 (%)</summary>
    public decimal DefaultAllocationPercent { get; set; } = 100;

    // ── 고급 설정 ──

    /// <summary>규칙 기반 청산 조건 (JSON: List&lt;EntryRule&gt;). 조건 충족 시 청산.</summary>
    public string ExitRulesJson { get; set; } = "[]";

    /// <summary>청산 조건 결합 방식: "OR"(하나라도 충족) 또는 "AND"(모두 충족)</summary>
    public string ExitRulesLogic { get; set; } = "OR";

    /// <summary>매도 조건 그룹 (JSON: List&lt;ConditionGroup&gt;). 비어있지 않으면 ExitRulesJson보다 우선 적용됩니다.</summary>
    public string ExitGroupsJson { get; set; } = "[]";

    /// <summary>매도 조건 그룹 간 결합 방식: "AND" 또는 "OR"</summary>
    public string ExitGroupsLogic { get; set; } = "OR";

    /// <summary>스케일링 규칙 (JSON: List&lt;ScalingRule&gt;)</summary>
    public string ScalingRulesJson { get; set; } = "[]";

    /// <summary>시간/계절 필터 (JSON: TimeFilter)</summary>
    public string TimeFilterJson { get; set; } = "{}";

    /// <summary>리스크 서킷브레이커 (JSON: CircuitBreakerConfig)</summary>
    public string CircuitBreakerJson { get; set; } = "{}";

    /// <summary>재진입 규칙 (JSON: ReentryConfig)</summary>
    public string ReentryJson { get; set; } = "{}";

    /// <summary>포트폴리오 레벨 규칙 (JSON: PortfolioRulesConfig)</summary>
    public string PortfolioRulesJson { get; set; } = "{}";

    /// <summary>진입 조건 그룹 (JSON: List&lt;ConditionGroup&gt;). 비어있지 않으면 EntryRulesJson보다 우선 적용됩니다.</summary>
    public string EntryGroupsJson { get; set; } = "[]";

    /// <summary>그룹 간 결합 방식: "AND" 또는 "OR"</summary>
    public string EntryGroupsLogic { get; set; } = "AND";

    /// <summary>동적 손절/목표 설정 (JSON: DynamicExitConfig)</summary>
    public string DynamicExitJson { get; set; } = "{}";

    /// <summary>진입 가격 모드: "CurrentClose"(기본), "NextOpen"(차기봉 시가 지연 체결; 시가에서 신호를 재평가하지 않음)</summary>
    public string EntryMode { get; set; } = "CurrentClose";

    /// <summary>
    /// 전략을 평가하고 실행할 기준 봉. 미리보기·백테스트·실시간 스캐너가 같은 값을 사용해야 한다.
    /// </summary>
    public TimeFrame TimeFrame { get; set; } = TimeFrame.Daily;

    /// <summary>포지션 사이징 모드: "FixedRisk"(기본), "Kelly", "HalfKelly"</summary>
    public string SizingMode { get; set; } = "FixedRisk";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 실시간 스캐너와 자동 주문에 이 전략을 연결할지 여부.
    /// 기존 연구 전략이 배포만으로 주문을 만들지 않도록 기본값은 false입니다.
    /// </summary>
    public bool EnableLiveTrading { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 비중 단계 하나를 정의합니다. 조건이 충족되면 해당 비중(%)으로 투자합니다.
/// </summary>
public class WeightTier
{
    /// <summary>단계 이름 (예: "공격적 매수", "방어적")</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>이 단계의 조건 목록 (EntryRule과 동일 형식)</summary>
    public List<EntryRule> Conditions { get; set; } = new();

    /// <summary>조건 결합 방식: "AND" 또는 "OR"</summary>
    public string Logic { get; set; } = "AND";

    /// <summary>투자 비중 (0~100%)</summary>
    public decimal AllocationPercent { get; set; } = 100;
}

/// <summary>
/// 진입 조건 하나를 정의합니다.
/// </summary>
public class EntryRule
{
    /// <summary>
    /// 지표 종류. 기본 지표:
    /// RSI, CUMULATIVE_RSI, PRICE_VS_SMA, PRICE_VS_EMA, MACD_HIST, BOLLINGER_POS,
    /// VOLUME_RATIO, PRICE_CHANGE, ATR, SMA_SLOPE, CANDLE_BODY
    ///
    /// 가격 구조:
    /// DIST_FROM_HIGH, DIST_FROM_LOW, GAP, HIGHER_LOW, LOWER_HIGH,
    /// INSIDE_BAR, ENGULFING, BREAKOUT_HIGH, BREAKOUT_LOW
    ///
    /// 모멘텀/추세:
    /// CONSECUTIVE_UP, CONSECUTIVE_DOWN, ADX, STOCHASTIC_K, STOCHASTIC_D
    ///
    /// 메타 조건:
    /// WITHIN_N_BARS (최근 N봉 내 조건 충족 여부)
    /// </summary>
    public string Indicator { get; set; } = string.Empty;

    /// <summary>지표 파라미터 (예: {"period": 14})</summary>
    public Dictionary<string, decimal> Params { get; set; } = new();

    /// <summary>비교 연산자: &gt;, &lt;, &gt;=, &lt;=, crosses_above, crosses_below</summary>
    public string Operator { get; set; } = ">";

    /// <summary>비교 기준값</summary>
    public decimal Value { get; set; }

    /// <summary>
    /// 최근 N봉 내 조건 충족 여부 체크 (0 = 현재 봉만, 양수 = 최근 N봉 중 하나라도 충족).
    /// 예: withinBars=3이면 최근 3봉 중 하나라도 조건을 만족하면 통과.
    /// </summary>
    public int WithinBars { get; set; }

    /// <summary>
    /// 참조 종목. 이 종목의 지표를 평가합니다. null/빈값 = 현재 매매 대상 종목.
    /// 예: "SPY" → SPY의 RSI를 기준으로 TQQQ 매매 판단
    /// </summary>
    public string? RefSymbol { get; set; }

    /// <summary>비교 대상이 스칼라가 아닌 다른 지표일 때. 비어있으면 Value와 비교.</summary>
    public string? CompareIndicator { get; set; }
    public Dictionary<string, decimal> CompareParams { get; set; } = new();

    /// <summary>규칙 가중치 (기본 1.0). 신뢰도 계산에 사용.</summary>
    public decimal Weight { get; set; } = 1.0m;

    /// <summary>N봉 연속 조건 충족 필요 (0=현재봉만, 양수=N봉 연속 충족). withinBars와 배타적.</summary>
    public int ConsecutiveBars { get; set; }
}

/// <summary>
/// 스케일링(피라미딩/분할매도) 규칙.
/// </summary>
public class ScalingRule
{
    /// <summary>"SCALE_IN" (추가 매수) 또는 "SCALE_OUT" (분할 매도)</summary>
    public string Direction { get; set; } = "SCALE_IN";

    /// <summary>스케일링 발동 조건 목록</summary>
    public List<EntryRule> Conditions { get; set; } = new();

    /// <summary>조건 결합: AND/OR</summary>
    public string Logic { get; set; } = "AND";

    /// <summary>원래 포지션 대비 비율 (%)</summary>
    public decimal Percent { get; set; } = 50;

    /// <summary>최대 스케일링 횟수</summary>
    public int MaxCount { get; set; } = 1;

    /// <summary>최소 수익률 (%). 이 수익률 이상일 때만 스케일인 허용.</summary>
    public decimal MinProfitPercent { get; set; }
}

/// <summary>
/// 시간/계절 필터. 특정 요일/월에만 매매하도록 제한합니다.
/// </summary>
public class TimeFilter
{
    /// <summary>허용 요일 (0=일~6=토). 비어있으면 모든 요일 허용</summary>
    public List<int> AllowedDaysOfWeek { get; set; } = new();

    /// <summary>차단 월 (1~12). 비어있으면 모든 월 허용</summary>
    public List<int> BlockedMonths { get; set; } = new();
}

/// <summary>
/// 리스크 서킷브레이커. 연속 손실 또는 최대 낙폭 초과 시 매매를 일시 중단합니다.
/// </summary>
public class CircuitBreakerConfig
{
    /// <summary>연속 손실 N회 후 쿨다운 (0=비활성)</summary>
    public int ConsecutiveLossLimit { get; set; }

    /// <summary>서킷브레이커 발동 후 대기 봉 수</summary>
    public int CooldownBars { get; set; } = 5;

    /// <summary>전략 최대 낙폭 % (0=비활성). 초과 시 매매 영구 중단</summary>
    public decimal MaxDrawdownPercent { get; set; }
}

/// <summary>
/// 재진입 규칙. 청산 후 같은 종목에 대한 쿨다운을 설정합니다.
/// </summary>
public class ReentryConfig
{
    /// <summary>손절 후 동일 종목 쿨다운 봉 수 (0=즉시 재진입 가능)</summary>
    public int CooldownBarsAfterLoss { get; set; }

    /// <summary>익절 후 동일 종목 쿨다운 봉 수</summary>
    public int CooldownBarsAfterWin { get; set; }
}

/// <summary>
/// 포트폴리오 레벨 규칙.
/// </summary>
public class PortfolioRulesConfig
{
    /// <summary>최대 동시 포지션 수 (0=기본 설정 사용)</summary>
    public int MaxTotalPositions { get; set; }

    /// <summary>단일 종목 최대 자본 비율 % (0=기본)</summary>
    public decimal MaxSinglePositionPercent { get; set; }

    /// <summary>하루 최대 진입 수 (0=무제한)</summary>
    public int MaxEntriesPerDay { get; set; }

    /// <summary>보유종목간 최대 상관계수 (0=비활성, 0.7 권장). 초과 시 진입 스킵.</summary>
    public decimal MaxCorrelation { get; set; }
}

/// <summary>
/// 조건 그룹. 규칙들을 그룹 단위로 묶어 2단계 논리를 구성합니다.
/// 예: (RSI &lt; 30 AND 거래량 > 2) OR (MACD 상향돌파 AND ADX > 25)
/// </summary>
public class ConditionGroup
{
    /// <summary>그룹 이름 (예: "과매도 반등", "모멘텀 진입")</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>이 그룹 내 규칙들</summary>
    public List<EntryRule> Rules { get; set; } = new();

    /// <summary>그룹 내 규칙 결합: "AND" 또는 "OR"</summary>
    public string Logic { get; set; } = "AND";
}

/// <summary>
/// 동적 손절/목표 설정. 지표 기반으로 손절/목표가를 계산합니다.
/// </summary>
public class DynamicExitConfig
{
    /// <summary>
    /// 손절 유형: "ATR" (기본), "BOLLINGER_LOWER", "SMA", "EMA", "PREV_LOW", "PERCENT"
    /// </summary>
    public string StopType { get; set; } = "ATR";

    /// <summary>손절 파라미터. ATR: {multiplier, period}, SMA/EMA: {period}, PERCENT: {percent}</summary>
    public Dictionary<string, decimal> StopParams { get; set; } = new();

    /// <summary>
    /// 목표 유형: "ATR" (기본), "BOLLINGER_UPPER", "SMA", "EMA", "PREV_HIGH", "R_MULTIPLE", "PERCENT"
    /// </summary>
    public string TargetType { get; set; } = "ATR";

    /// <summary>목표 파라미터. ATR: {multiplier, period}, R_MULTIPLE: {multiple}, PERCENT: {percent}</summary>
    public Dictionary<string, decimal> TargetParams { get; set; } = new();
}
