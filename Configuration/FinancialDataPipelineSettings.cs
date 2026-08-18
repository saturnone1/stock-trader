namespace StockTrader.Configuration;

public class FinancialDataPipelineSettings
{
    public bool Enabled { get; set; } = true;
    public int ScanIntervalMinutes { get; set; } = 15;
    public string ImportDirectory { get; set; } = string.Empty;
    public bool VendorSyncEnabled { get; set; } = false;
    public int VendorSyncIntervalHours { get; set; } = 24;
    public int VendorSymbolLimit { get; set; } = 50;
    public string VendorSymbols { get; set; } = string.Empty;
    public string VendorUserAgent { get; set; } = "StockTrader/1.0 (contact@example.com)";
}
