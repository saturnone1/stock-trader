using StockTrader.Models.Enums;

namespace StockTrader.Models;

public class OhlcvBar
{
    public long Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public TimeFrame TimeFrame { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public decimal? Vwap { get; set; }

    public decimal Range => High - Low;
    public bool IsBullish => Close > Open;
    public decimal Body => Math.Abs(Close - Open);
}
