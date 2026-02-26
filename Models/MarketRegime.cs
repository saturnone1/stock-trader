namespace StockTrader.Models;

public class MarketRegime
{
    public bool SpyAbove200Ma { get; set; }
    public decimal SpyPrice { get; set; }
    public decimal Spy200Ma { get; set; }
    public decimal VixLevel { get; set; }
    public string RegimeLabel { get; set; } = "Unknown";
    public DateTime AsOf { get; set; }

    // ML 기반 분류 결과 (ML 모델 미사용 시 null/기본값)
    public int MlClusterId { get; set; } = -1;
    public string MlRegimeLabel { get; set; } = string.Empty;
}
