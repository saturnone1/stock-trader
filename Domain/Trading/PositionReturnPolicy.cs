namespace StockTrader.Domain.Trading;

public static class PositionReturnPolicy
{
    public static decimal Calculate(
        decimal entryPrice,
        int quantity,
        decimal unrealizedPnL)
    {
        var entryNotional = entryPrice * quantity;
        return entryNotional > 0m ? unrealizedPnL / entryNotional : 0m;
    }
}
