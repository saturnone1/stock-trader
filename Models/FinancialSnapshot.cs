namespace StockTrader.Models;

public class FinancialSnapshot
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTime AsOfDate { get; set; }
    public string Source { get; set; } = "Manual";

    public decimal? PeRatio { get; set; }
    public decimal? PbRatio { get; set; }
    public decimal? RoePercent { get; set; }
    public decimal? OperatingMarginPercent { get; set; }

    public decimal? RevenueCurrent { get; set; }
    public decimal? RevenuePrevious { get; set; }
    public decimal? OperatingIncomeCurrent { get; set; }
    public decimal? OperatingIncomePrevious { get; set; }
    public decimal? NetIncomeCurrent { get; set; }
    public decimal? NetIncomePrevious { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
