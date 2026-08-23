using StockTrader.Domain.Backtesting;
using StockTrader.Engine.Execution;
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
    private readonly ExecutionCostLedger<TradeRecord> _costs = new(
        slippageModel, slippagePercent, commissionPerTrade);

    public decimal TotalSlippage => _costs.TotalSlippage;
    public decimal TotalCommission => _costs.TotalCommission;

    public void ApplyNewTrades(
        IReadOnlyList<TradeRecord> trades,
        int startIndex,
        Action<TradeRecord> onCostApplied)
    {
        for (var tradeIndex = startIndex; tradeIndex < trades.Count; tradeIndex++)
        {
            var trade = trades[tradeIndex];
            var result = _costs.TrySettle(trade, new ExecutionCostInput(
                trade.EntryPrice,
                trade.ExitPrice,
                trade.Quantity,
                trade.PnL,
                trade.EntryAtr,
                trade.EntryVolume));
            if (result is null) continue;

            trade.PnL = result.Value.NetPnl;
            trade.PnLPercent = result.Value.ReturnFraction;
            onCostApplied(trade);
        }
    }
}
