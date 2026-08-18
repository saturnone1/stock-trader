using StockTrader.Models.Enums;

namespace StockTrader.Models;

public class TradeRecommendation
{
    public long Id { get; set; }
    /// <summary>이 추천을 만든 PatternSignal.Id. 동일 시그널의 중복 주문을 막는 멱등 키.</summary>
    public long? SourceSignalId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public PatternType PatternType { get; set; }
    public string? CustomPatternName { get; set; }
    public DateTime GeneratedAt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal PositionSize { get; set; }
    public int ShareQuantity { get; set; }
    public decimal Expectancy { get; set; }
    public bool WasExecuted { get; set; }
    public OrderMode Mode { get; set; }

    /// <summary>브로커 호출 전에 영속적으로 선점한 신규 진입 요청 시각.</summary>
    public DateTime? EntryRequestedAt { get; set; }

    /// <summary>신규 진입 요청 시점에 고정한 TradingAccount.Id.</summary>
    public int? EntryAccountId { get; set; }

    /// <summary>브로커가 반환한 신규 진입 주문 ID. 재시작 후 체결 재조정에 사용한다.</summary>
    public string? EntryOrderId { get; set; }

    /// <summary>마지막으로 확정된 제출 실패 또는 운영자 확인이 필요한 이유.</summary>
    public string? EntryExecutionNote { get; set; }

    public decimal StopLossPercent => EntryPrice != 0
        ? Math.Abs(EntryPrice - StopLossPrice) / EntryPrice
        : 0;
    public decimal RiskRewardRatio => StopLossPercent != 0
        ? ((TargetPrice - EntryPrice) / EntryPrice) / StopLossPercent
        : 0;
}
