using StockTrader.Models.Enums;

namespace StockTrader.Models;

/// <summary>
/// 종목별 트레이딩 프로파일.
/// 백테스트로 최적화한 패턴 조합 + 비중 전략 + 파라미터를 종목별로 저장하고,
/// 실매매 시 해당 종목에 맞는 설정을 자동 적용합니다.
/// </summary>
public class SymbolProfile
{
    public long Id { get; set; }

    /// <summary>종목 심볼 (예: TQQQ, AAPL)</summary>
    public string Symbol { get; set; } = "";

    /// <summary>프로파일 이름 (예: "기본", "공격형", "보수적")</summary>
    public string Name { get; set; } = SymbolProfilePolicy.DefaultName;

    /// <summary>이 프로파일이 실매매에 활성화되어 있는지 여부. 종목당 1개만 활성.</summary>
    public bool IsActive { get; set; }

    // ── 패턴 설정 ──────────────────────────────────────────────────────
    /// <summary>이 종목에 사용할 패턴 목록</summary>
    public List<PatternType> EnabledPatterns { get; set; } = new();

    /// <summary>패턴별 파라미터 오버라이드 (PatternParameterOverrides JSON)</summary>
    public string? ParameterOverridesJson { get; set; }

    // ── 비중 관리 전략 ─────────────────────────────────────────────────
    /// <summary>비중 관리 전략 파라미터 (WeightStrategy JSON). null이면 비중 조절 미적용.</summary>
    public string? WeightStrategyJson { get; set; }

    // ── 리스크 파라미터 ────────────────────────────────────────────────
    public decimal RiskPerTradePercent { get; set; } = SymbolProfilePolicy.DefaultRiskPerTradePercent;
    public int MaxTotalPositions { get; set; } = SymbolProfilePolicy.DefaultMaximumPositions;

    // ── 백테스트 결과 스냅샷 ───────────────────────────────────────────
    public decimal? BacktestReturnPct { get; set; }
    public decimal? BacktestWinRate { get; set; }
    public decimal? BacktestMaxDrawdown { get; set; }
    public decimal? BacktestSharpe { get; set; }
    public int? BacktestTrades { get; set; }
    public DateTime? BacktestFrom { get; set; }
    public DateTime? BacktestTo { get; set; }

    // ── 메타 ──────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
