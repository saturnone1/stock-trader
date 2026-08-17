namespace StockTrader.Models;

public class PatternSignal
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public string? CustomPatternName { get; set; }
    public DateTime DetectedAt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal Confidence { get; set; }
    public string Details { get; set; } = string.Empty;
    public bool IsActive { get; set; }

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
