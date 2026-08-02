namespace StockTrader.Models;

public class FinancialImportRun
{
    public long Id { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
