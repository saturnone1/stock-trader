using StockTrader.Models.Enums;

namespace StockTrader.Models;

public class TradeRecommendation
{
    public long Id { get; set; }
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

    public decimal StopLossPercent => EntryPrice != 0
        ? Math.Abs(EntryPrice - StopLossPrice) / EntryPrice
        : 0;
    public decimal RiskRewardRatio => StopLossPercent != 0
        ? ((TargetPrice - EntryPrice) / EntryPrice) / StopLossPercent
        : 0;
}
