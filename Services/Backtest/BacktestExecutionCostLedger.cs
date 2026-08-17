using StockTrader.Models;
using StockTrader.Models.Enums;

namespace StockTrader.Services.Backtest;

/// <summary>
/// 완료된 거래에 슬리피지와 수수료를 한 번만 반영하고 비용 합계를 보관합니다.
/// </summary>
internal sealed class BacktestExecutionCostLedger(
    SlippageModel slippageModel,
    decimal slippagePercent,
    decimal commissionPerTrade)
{
    private readonly Dictionary<TradeRecord, ExecutionCost> _costs = [];

    public decimal TotalSlippage => _costs.Values.Sum(cost => cost.Slippage);
    public decimal TotalCommission => _costs.Values.Sum(cost => cost.Commission);

    public void ApplyNewTrades(
        IReadOnlyList<TradeRecord> trades,
        int startIndex,
        Action<TradeRecord> onCostApplied)
    {
        for (var tradeIndex = startIndex; tradeIndex < trades.Count; tradeIndex++)
        {
            var trade = trades[tradeIndex];
            if (_costs.ContainsKey(trade)) continue;

            var slippage = CalculateSlippage(trade);
            trade.PnL -= slippage + commissionPerTrade;
            trade.PnLPercent = trade.EntryPrice > 0 && trade.Quantity > 0
                ? trade.PnL / (trade.EntryPrice * trade.Quantity)
                : 0;

            _costs[trade] = new ExecutionCost(slippage, commissionPerTrade);
            onCostApplied(trade);
        }
    }

    private decimal CalculateSlippage(TradeRecord trade)
    {
        if (slippageModel != SlippageModel.Adaptive || trade.EntryAtr <= 0 || trade.EntryPrice <= 0)
        {
            return (trade.EntryPrice + trade.ExitPrice)
                * (slippagePercent / 100m) * trade.Quantity;
        }

        var atrPercent = trade.EntryAtr / trade.EntryPrice;
        var volatilityFactor = Math.Clamp(atrPercent / 0.02m, 0.5m, 3.0m);
        var liquidityFactor = 1.0m;
        if (trade.EntryVolume > 0)
        {
            var orderRatio = (decimal)trade.Quantity / trade.EntryVolume;
            var squareRootImpact = (decimal)Math.Sqrt((double)Math.Max(0m, orderRatio));
            liquidityFactor = Math.Clamp(1.0m + squareRootImpact * 2.0m, 0.5m, 3.0m);
        }

        var adaptivePercent = slippagePercent / 100m * volatilityFactor * liquidityFactor;
        return (trade.EntryPrice + trade.ExitPrice) * adaptivePercent * trade.Quantity;
    }

    private sealed record ExecutionCost(decimal Slippage, decimal Commission);
}
