namespace StockTrader.Models;

public class OptimizationResult
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int Rank { get; set; }

    /// <summary>파라미터 — OptimizeParamSnapshot JSON</summary>
    public string ParamsJson { get; set; } = "";

    // IS 성과
    public decimal TotalReturn { get; set; }
    public decimal SortinoRatio { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal CalmarRatio { get; set; }
    public decimal AnnualizedReturn { get; set; }

    // OOS 성과 (nullable)
    public decimal? OosTotalReturn { get; set; }
    public decimal? OosSortinoRatio { get; set; }
    public decimal? OosSharpeRatio { get; set; }
    public decimal? OosMaxDrawdown { get; set; }
    public decimal? OosWinRate { get; set; }
    public int? OosTotalTrades { get; set; }
    public decimal? OosProfitFactor { get; set; }
    public decimal? OosCalmarRatio { get; set; }
    public decimal? OosAnnualizedReturn { get; set; }

    // 메타
    public long TestedAtCombination { get; set; }
    public DateTime DiscoveredAt { get; set; }

    // Navigation
    public OptimizationJob Job { get; set; } = null!;
}
