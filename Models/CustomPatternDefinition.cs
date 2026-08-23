using StockTrader.Domain.Strategies;
using StockTrader.Models.Enums;

namespace StockTrader.Models;

/// <summary>
/// 사용자 정의 패턴. 지표 조건을 조합하여 자신만의 매매 전략을 정의합니다.
/// </summary>
public class CustomPatternDefinition
{
    public int Id { get; set; }

    /// <summary>
    /// 저장 전략 문서 형식 버전. 실행 엔진 버전과 독립적으로 호환 읽기와 향후 업그레이드를 결정한다.
    /// </summary>
    public int DocumentVersion { get; set; } = StrategyDocumentVersions.Current;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 이름의 서버 관리 비교 키. 동시 저장에서도 대소문자 무시 고유성을 보장한다.
    /// API 계약에는 노출하지 않는다.
    /// </summary>
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>진입 조건 목록 (JSON: List&lt;EntryRule&gt;)</summary>
    public string EntryRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;

    /// <summary>진입 조건 결합 방식: "AND" 또는 "OR"</summary>
    public string EntryLogic { get; set; } = StrategyDocumentDefaults.AndLogic;

    /// <summary>강세장(SPY > 200SMA) 필터 사용 여부</summary>
    public bool RequireBullRegime { get; set; }

    // ── 청산 설정 ──
    public decimal AtrStopMultiplier { get; set; } = StrategyDocumentDefaults.AtrStopMultiplier;
    public decimal AtrTargetMultiplier { get; set; } = StrategyDocumentDefaults.AtrTargetMultiplier;
    public int MaxHoldingBars { get; set; } = StrategyDocumentDefaults.MaxHoldingBars;
    public decimal TrailingAtr { get; set; }
    public decimal PartialProfitR { get; set; }

    // ── 비중 단계 설정 ──

    /// <summary>비중 단계 사용 여부</summary>
    public bool UseWeightTiers { get; set; }

    /// <summary>비중 단계 목록 (JSON: List&lt;WeightTier&gt;). 위에서부터 순서대로 평가, 첫 매칭 적용.</summary>
    public string WeightTiersJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;

    /// <summary>어떤 비중 단계에도 매칭 안 될 때 기본 투자 비중 (%)</summary>
    public decimal DefaultAllocationPercent { get; set; } = StrategyDocumentDefaults.DefaultAllocationPercent;

    // ── 고급 설정 ──

    /// <summary>규칙 기반 청산 조건 (JSON: List&lt;EntryRule&gt;). 조건 충족 시 청산.</summary>
    public string ExitRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;

    /// <summary>청산 조건 결합 방식: "OR"(하나라도 충족) 또는 "AND"(모두 충족)</summary>
    public string ExitRulesLogic { get; set; } = StrategyDocumentDefaults.OrLogic;

    /// <summary>매도 조건 그룹 (JSON: List&lt;ConditionGroup&gt;). 비어있지 않으면 ExitRulesJson보다 우선 적용됩니다.</summary>
    public string ExitGroupsJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;

    /// <summary>매도 조건 그룹 간 결합 방식: "AND" 또는 "OR"</summary>
    public string ExitGroupsLogic { get; set; } = StrategyDocumentDefaults.OrLogic;

    /// <summary>스케일링 규칙 (JSON: List&lt;ScalingRule&gt;)</summary>
    public string ScalingRulesJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;

    /// <summary>시간/계절 필터 (JSON: TimeFilter)</summary>
    public string TimeFilterJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;

    /// <summary>리스크 서킷브레이커 (JSON: CircuitBreakerConfig)</summary>
    public string CircuitBreakerJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;

    /// <summary>재진입 규칙 (JSON: ReentryConfig)</summary>
    public string ReentryJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;

    /// <summary>포트폴리오 레벨 규칙 (JSON: PortfolioRulesConfig)</summary>
    public string PortfolioRulesJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;

    /// <summary>진입 조건 그룹 (JSON: List&lt;ConditionGroup&gt;). 비어있지 않으면 EntryRulesJson보다 우선 적용됩니다.</summary>
    public string EntryGroupsJson { get; set; } = StrategyDocumentDefaults.EmptyListJson;

    /// <summary>그룹 간 결합 방식: "AND" 또는 "OR"</summary>
    public string EntryGroupsLogic { get; set; } = StrategyDocumentDefaults.AndLogic;

    /// <summary>동적 손절/목표 설정 (JSON: DynamicExitConfig)</summary>
    public string DynamicExitJson { get; set; } = StrategyDocumentDefaults.EmptyObjectJson;

    /// <summary>진입 가격 모드: "CurrentClose"(기본), "NextOpen"(차기봉 시가 지연 체결; 시가에서 신호를 재평가하지 않음)</summary>
    public string EntryMode { get; set; } = StrategyCatalog.CurrentCloseEntryMode;

    /// <summary>
    /// 전략을 평가하고 실행할 기준 봉. 미리보기·백테스트·실시간 스캐너가 같은 값을 사용해야 한다.
    /// </summary>
    public TimeFrame TimeFrame { get; set; } = TimeFrame.Daily;

    /// <summary>포지션 사이징 모드: "FixedRisk"(기본), "Kelly", "HalfKelly"</summary>
    public string SizingMode { get; set; } = StrategyCatalog.FixedRiskSizingMode;

    public bool IsActive { get; set; } = StrategyDocumentDefaults.IsActive;

    /// <summary>
    /// 실시간 스캐너와 자동 주문에 이 전략을 연결할지 여부.
    /// 기존 연구 전략이 배포만으로 주문을 만들지 않도록 기본값은 false입니다.
    /// </summary>
    public bool EnableLiveTrading { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
