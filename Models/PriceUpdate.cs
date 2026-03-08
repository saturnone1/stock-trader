namespace StockTrader.Models;

public record PriceUpdate(
    string Symbol,
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    long Volume,
    DateTime Timestamp);
