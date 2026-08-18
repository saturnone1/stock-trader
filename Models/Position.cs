using StockTrader.Models.Enums;

namespace StockTrader.Models;

public class Position
{
    public long Id { get; set; }

    /// <summary>
    /// 이 포지션을 소유하는 TradingAccount.Id.
    /// 0은 계좌 미지정(레거시 데이터 또는 단일 계좌 운용).
    /// </summary>
    public int AccountId { get; set; }

    public string Symbol { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>최초 체결 수량. 부분 청산 뒤에도 원래 포지션 규모를 보존한다.</summary>
    public int InitialQuantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public PatternType PatternType { get; set; }
    public string? CustomPatternName { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal? ExitPrice { get; set; }

    /// <summary>진입 이후 최고가 (트레일링 스탑용). 0이면 미추적.</summary>
    public decimal HighSinceEntry { get; set; }

    /// <summary>진입 시 ATR 값 (트레일링/손익비 계산용).</summary>
    public decimal EntryAtr { get; set; }

    /// <summary>진입 시 확정된 1주당 위험거리. 보호 손절이 움직여도 변경하지 않는다.</summary>
    public decimal InitialRiskDistance { get; set; }

    /// <summary>손익분기 손절이 적용됐는지 여부. 서비스 재시작 후에도 유지한다.</summary>
    public bool BreakevenApplied { get; set; }

    /// <summary>추적손절이 활성화됐는지 여부. 서비스 재시작 후에도 유지한다.</summary>
    public bool TrailingStopActivated { get; set; }

    /// <summary>전략의 1차 이익실현이 체결됐는지 여부.</summary>
    public bool PartialProfitTaken { get; set; }

    /// <summary>브로커 주문 전에 기록하는 내구성 있는 포지션 실행 의도 시각.</summary>
    public DateTime? ExecutionRequestedAt { get; set; }

    /// <summary>포지션 실행 의도를 발생시킨 전략 사유.</summary>
    public string? ExecutionRequestReason { get; set; }

    /// <summary>이번 주문에서 매수 또는 매도하도록 청구한 수량.</summary>
    public int? ExecutionRequestQuantity { get; set; }

    /// <summary>이번 체결이 전략의 1차 이익실현 상태를 확정하는지 여부.</summary>
    public bool ExecutionRequestMarksPartialProfit { get; set; }

    /// <summary>이번 실행의 종류. 기존 대기 청산은 null을 전량 청산으로 해석한다.</summary>
    public PositionExecutionKind? ExecutionRequestKind { get; set; }

    /// <summary>스케일링 실행이면 해당 컴파일 전략의 규칙 인덱스.</summary>
    public int? ExecutionRequestRuleIndex { get; set; }

    /// <summary>브로커가 반환한 주문 ID. 재시작 후 체결 재조정에 사용한다.</summary>
    public string? ExecutionOrderId { get; set; }

    public List<PositionScalingExecution> ScalingExecutions { get; set; } = [];

    public IReadOnlyDictionary<int, int> ScalingExecutionCounts => ScalingExecutions
        .ToDictionary(item => item.RuleIndex, item => item.ExecutionCount);

    public bool IsOpen => ClosedAt == null;
    public decimal UnrealizedPnL => (CurrentPrice - EntryPrice) * Quantity;
    public decimal? RealizedPnL => ExitPrice.HasValue
        ? (ExitPrice.Value - EntryPrice) * Quantity
        : null;
}
