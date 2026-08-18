namespace StockTrader.Models;

public class PatternSignal
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public string? CustomPatternName { get; set; }
    /// <summary>
    /// 전략 조건이 충족된 OHLCV 봉의 시각입니다. 과거 데이터로 다시 평가해도
    /// 동일해야 하며, 동일 봉 신호의 영속 식별자로 사용합니다.
    /// 기존 레코드는 값이 없을 수 있습니다.
    /// </summary>
    public DateTime? SignalBarAt { get; set; }
    /// <summary>
    /// 실행 시스템이 신호를 관측한 시각입니다. 실시간 신호 신선도 판단에 사용합니다.
    /// </summary>
    public DateTime DetectedAt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal Confidence { get; set; }
    public string Details { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>
    /// 멱등 봉 식별자가 도입되기 전에 반복 저장된 동일 활동 중 대체된 행입니다.
    /// 감사 이력은 보존하되 조회·수동 주문에서는 제외합니다.
    /// </summary>
    public bool IsSuperseded { get; set; }

    /// <summary>
    /// 진입 판단 시점에 계산한 ML 피처 스키마입니다. null인 기존 행은 학습에서 제외됩니다.
    /// 피처는 결과가 알려지기 전에 기록되어야 하며 청산 정보로 다시 계산하지 않습니다.
    /// </summary>
    public int? ScoringFeatureVersion { get; set; }
    public float? ScoringRsi { get; set; }
    public float? ScoringBollingerPosition { get; set; }
    public float? ScoringVolumeRatio { get; set; }
    public float? ScoringMarketRegimeCode { get; set; }
    public float? ScoringAtrPercent { get; set; }
    public float? ScoringHistoricalWinRate { get; set; }
    public float? ScoringRiskRewardRatio { get; set; }
    public float? ScoringPriceVsLongMovingAverage { get; set; }
    public float? ScoringLongTrendHistoryAvailable { get; set; }

    /// <summary>
    /// 비중 단계에 의한 투자 비중 스케일 (0.0~1.0). 기본 1.0 = 100%.
    /// DB에 저장하지 않으며, 백테스트 시뮬레이션에서만 사용합니다.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public decimal AllocationScale { get; set; } = 1.0m;

    /// <summary>차기봉 진입 대기 중 여부 (백테스트 전용, DB 미저장)</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool PendingEntry { get; set; }
}
