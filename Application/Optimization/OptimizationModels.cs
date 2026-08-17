using StockTrader.Models;
using StockTrader.Models.Enums;
using StockTrader.Application.Strategies;

namespace StockTrader.Application.Optimization;

// ── 파라미터 범위/후보값 정의 ─────────────────────────────────────────────────

/// <summary>
/// 단일 숫자 파라미터의 최적화 범위. min/max/step 또는 values 중 하나를 사용합니다.
/// </summary>
public class ParamRange
{
    /// <summary>범위 시작값 (min/max/step 방식)</summary>
    public decimal? Min { get; set; }
    /// <summary>범위 끝값</summary>
    public decimal? Max { get; set; }
    /// <summary>스텝 간격 (기본 1)</summary>
    public decimal? Step { get; set; }
    /// <summary>명시적 후보값 목록 (values 방식). 설정되면 Min/Max/Step은 무시됩니다.</summary>
    public List<decimal>? Values { get; set; }

    /// <summary>이 범위에서 모든 후보값 열거</summary>
    public IEnumerable<decimal> Enumerate()
    {
        if (Values is { Count: > 0 })
            return Values;

        if (Min == null || Max == null)
            return Enumerable.Empty<decimal>();

        var step = Step ?? 1m;
        if (step <= 0) step = 1m;

        var result = new List<decimal>();
        for (var v = Min.Value; v <= Max.Value + step * 0.001m; v += step)
            result.Add(Math.Round(v, 6));
        return result;
    }
}

/// <summary>
/// 진입 룰의 특정 파라미터 오버라이드 범위.
/// </summary>
public class RuleParamRange
{
    /// <summary>대상 룰 범위: "Entry"(기본) 또는 "Exit"</summary>
    public string Scope { get; set; } = "Entry";
    /// <summary>EntryRulesJson 내 룰 인덱스 (0-based)</summary>
    public int RuleIndex { get; set; }
    /// <summary>오버라이드할 파라미터 키 (EntryRule.Params 딕셔너리의 키, 비교지표는 compare.{key} 형식)</summary>
    public string ParamKey { get; set; } = string.Empty;
    /// <summary>후보값 목록</summary>
    public List<decimal> Values { get; set; } = new();
}

/// <summary>
/// 진입 룰의 특정 필드(value, withinBars, weight, consecutiveBars, operator 등)를 오버라이드하는 범위.
/// </summary>
public class RuleFieldRange
{
    /// <summary>대상 룰 범위: "Entry"(기본) 또는 "Exit"</summary>
    public string Scope { get; set; } = "Entry";
    /// <summary>EntryRulesJson 내 룰 인덱스 (0-based)</summary>
    public int RuleIndex { get; set; }
    /// <summary>오버라이드할 필드명: "value", "withinBars", "weight", "consecutiveBars", "operator", "compareIndicator"</summary>
    public string FieldName { get; set; } = string.Empty;
    /// <summary>숫자 후보값 목록 (value, withinBars, weight, consecutiveBars 용)</summary>
    public List<decimal>? NumericValues { get; set; }
    /// <summary>문자열 후보값 목록 (operator, compareIndicator 용)</summary>
    public List<string>? StringValues { get; set; }
}

/// <summary>
/// 최적화할 파라미터 집합 정의
/// </summary>
public class OptimizeParams
{
    // ── 기존 숫자형 파라미터 ──
    public ParamRange? AtrStopMultiplier { get; set; }
    public ParamRange? AtrTargetMultiplier { get; set; }
    public ParamRange? MaxHoldingBars { get; set; }
    public ParamRange? TrailingAtr { get; set; }
    public ParamRange? PartialProfitR { get; set; }
    /// <summary>진입 룰별 파라미터 오버라이드 범위 목록</summary>
    public List<RuleParamRange>? RuleParamOverrides { get; set; }

    // ── 카테고리형 파라미터 ──
    /// <summary>테스트할 EntryLogic 후보: "AND", "OR"</summary>
    public List<string>? EntryLogicOptions { get; set; }
    /// <summary>테스트할 RequireBullRegime 후보: true, false</summary>
    public List<bool>? RequireBullRegimeOptions { get; set; }
    /// <summary>테스트할 EntryMode 후보: "CurrentClose", "NextOpen"</summary>
    public List<string>? EntryModeOptions { get; set; }
    /// <summary>테스트할 SizingMode 후보: "FixedRisk", "Kelly", "HalfKelly"</summary>
    public List<string>? SizingModeOptions { get; set; }
    /// <summary>테스트할 ExitLogic 후보: "AND", "OR"</summary>
    public List<string>? ExitLogicOptions { get; set; }
    /// <summary>테스트할 TimeFrame 후보: 0=1분, 1=5분, 2=15분, 3=일봉, 4=주봉</summary>
    public List<int>? TimeFrameOptions { get; set; }

    // ── 추가 숫자형 파라미터 ──
    /// <summary>기본 비중 % 범위</summary>
    public ParamRange? DefaultAllocationPercent { get; set; }
    /// <summary>연속 손실 한도 범위</summary>
    public ParamRange? CircuitBreakerConsecutiveLossLimit { get; set; }
    /// <summary>서킷브레이커 냉각 봉 수 범위</summary>
    public ParamRange? CircuitBreakerCooldownBars { get; set; }
    /// <summary>서킷브레이커 최대 MDD % 범위</summary>
    public ParamRange? CircuitBreakerMaxDrawdownPercent { get; set; }
    /// <summary>손실 후 재진입 냉각 봉 수 범위</summary>
    public ParamRange? ReentryCooldownAfterLoss { get; set; }
    /// <summary>승리 후 재진입 냉각 봉 수 범위</summary>
    public ParamRange? ReentryCooldownAfterWin { get; set; }
    /// <summary>최대 포지션 수 범위</summary>
    public ParamRange? PortfolioMaxPositions { get; set; }
    /// <summary>단일 포지션 최대 비중 % 범위</summary>
    public ParamRange? PortfolioMaxSinglePercent { get; set; }
    /// <summary>일일 최대 진입 수 범위</summary>
    public ParamRange? PortfolioMaxEntriesPerDay { get; set; }

    // ── 룰 필드 오버라이드 ──
    /// <summary>룰의 특정 필드(value, withinBars, weight, operator 등)를 오버라이드하는 범위 목록</summary>
    public List<RuleFieldRange>? RuleFieldOverrides { get; set; }
}

/// <summary>
/// 최적화 요청
/// </summary>
public class OptimizeRequest
{
    /// <summary>최적화할 기반 커스텀 패턴</summary>
    public StrategyDocument BasePattern { get; set; } = new();
    public List<string> Symbols { get; set; } = new();
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal InitialCapital { get; set; } = 100_000m;
    public DataSource? DataSource { get; set; }
    public TimeFrame TimeFrame { get; set; } = TimeFrame.Daily;

    /// <summary>최적화할 파라미터 범위 정의</summary>
    public OptimizeParams OptimizeParams { get; set; } = new();

    /// <summary>결과 정렬 기준: totalReturn, sortinoRatio, sharpeRatio, calmarRatio, profitFactor, winRate</summary>
    public string RankBy { get; set; } = "sortinoRatio";

    /// <summary>반환할 상위 결과 수 (기본 10)</summary>
    public int MaxResults { get; set; } = 10;

    /// <summary>최대 테스트 조합 수. 초과 시 재현 가능한 균등 표본을 선택합니다 (기본 500).</summary>
    public int MaxCombinations { get; set; } = 500;

    /// <summary>Out-of-Sample 검증 기간 비율 (0~0.5). 0이면 OOS 없이 전체 기간으로 최적화. 기본 0.25 (25%)</summary>
    public decimal OosPercent { get; set; } = 0.25m;
}

/// <summary>
/// 단일 조합의 파라미터 스냅샷
/// </summary>
public class OptimizeParamSnapshot
{
    // ── 기존 숫자형 ──
    public decimal? AtrStopMultiplier { get; set; }
    public decimal? AtrTargetMultiplier { get; set; }
    public int? MaxHoldingBars { get; set; }
    public decimal? TrailingAtr { get; set; }
    public decimal? PartialProfitR { get; set; }
    /// <summary>ruleIndex, paramKey, value 세트 목록</summary>
    public List<RuleOverrideEntry> RuleOverrides { get; set; } = new();

    // ── 카테고리형 ──
    public string? EntryLogic { get; set; }
    public bool? RequireBullRegime { get; set; }
    public string? EntryMode { get; set; }
    public string? SizingMode { get; set; }
    public string? ExitLogic { get; set; }
    /// <summary>타임프레임 (null이면 요청의 기본값 사용). 0=1분, 1=5분, 2=15분, 3=일봉, 4=주봉</summary>
    public int? TimeFrame { get; set; }

    // ── 추가 숫자형 ──
    public decimal? DefaultAllocationPercent { get; set; }
    public int? CircuitBreakerConsecutiveLossLimit { get; set; }
    public int? CircuitBreakerCooldownBars { get; set; }
    public decimal? CircuitBreakerMaxDrawdownPercent { get; set; }
    public int? ReentryCooldownAfterLoss { get; set; }
    public int? ReentryCooldownAfterWin { get; set; }
    public int? PortfolioMaxPositions { get; set; }
    public decimal? PortfolioMaxSinglePercent { get; set; }
    public int? PortfolioMaxEntriesPerDay { get; set; }

    // ── 룰 필드 오버라이드 ──
    public List<RuleFieldOverrideEntry>? RuleFieldOverrides { get; set; }
}

public class RuleOverrideEntry
{
    public string Scope { get; set; } = "Entry";
    public int RuleIndex { get; set; }
    public string ParamKey { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

/// <summary>단일 룰 필드 오버라이드 결과 엔트리</summary>
public class RuleFieldOverrideEntry
{
    public string Scope { get; set; } = "Entry";
    public int RuleIndex { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public string? StringValue { get; set; }
}

/// <summary>
/// 단일 조합의 백테스트 결과
/// </summary>
public class OptimizeResultItem
{
    public int Rank { get; set; }
    public OptimizeParamSnapshot Params { get; set; } = new();
    public decimal TotalReturn { get; set; }
    public decimal SortinoRatio { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal CalmarRatio { get; set; }
    public decimal AnnualizedReturn { get; set; }

    // OOS 검증 결과 (OOS가 비활성이면 null)
    public decimal? OosTotalReturn { get; set; }
    public decimal? OosSortinoRatio { get; set; }
    public decimal? OosSharpeRatio { get; set; }
    public decimal? OosMaxDrawdown { get; set; }
    public decimal? OosWinRate { get; set; }
    public int? OosTotalTrades { get; set; }
    public decimal? OosProfitFactor { get; set; }
    public decimal? OosCalmarRatio { get; set; }
    public decimal? OosAnnualizedReturn { get; set; }
}

/// <summary>
/// 최적화 응답
/// </summary>
public class OptimizeResponse
{
    public int TotalCombinations { get; set; }
    public int TestedCombinations { get; set; }
    public long ElapsedMs { get; set; }
    public List<OptimizeResultItem> Results { get; set; } = new();
    public DateTime? IsFrom { get; set; }
    public DateTime? IsTo { get; set; }
    public DateTime? OosFrom { get; set; }
    public DateTime? OosTo { get; set; }
}
