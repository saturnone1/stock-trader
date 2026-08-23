using StockTrader.Domain.Backtesting;

namespace StockTrader.Engine.Execution;

public readonly record struct ExecutionCostInput(
    decimal EntryPrice,
    decimal ExitPrice,
    int Quantity,
    decimal GrossPnl,
    decimal EntryAtr,
    long EntryVolume);

public readonly record struct ExecutionCostResult(
    decimal NetPnl,
    decimal ReturnFraction,
    decimal Slippage,
    decimal Commission);

/// <summary>
/// 하나의 체결 키에 비용을 정확히 한 번 적용하는 저장소 독립 원장입니다. 비용은 결과가
/// 생성되는 체결 시점에 순손익과 수익률에 반영됩니다.
/// </summary>
public sealed class ExecutionCostLedger<TKey>(
    SlippageModel slippageModel,
    decimal slippagePercent,
    decimal commissionPerExecution)
    where TKey : notnull
{
    private readonly Dictionary<TKey, ExecutionCostResult> _settled = [];

    public decimal TotalSlippage => _settled.Values.Sum(value => value.Slippage);
    public decimal TotalCommission => _settled.Values.Sum(value => value.Commission);

    public ExecutionCostResult? TrySettle(TKey key, ExecutionCostInput input)
    {
        if (_settled.ContainsKey(key)) return null;

        var slippage = CalculateSlippage(input);
        var netPnl = input.GrossPnl - slippage - commissionPerExecution;
        var notional = input.EntryPrice * input.Quantity;
        var result = new ExecutionCostResult(
            netPnl,
            notional > 0 ? netPnl / notional : 0m,
            slippage,
            commissionPerExecution);
        _settled.Add(key, result);
        return result;
    }

    private decimal CalculateSlippage(ExecutionCostInput input)
    {
        if (slippageModel != SlippageModel.Adaptive || input.EntryAtr <= 0 || input.EntryPrice <= 0)
        {
            return (input.EntryPrice + input.ExitPrice)
                * (slippagePercent / 100m) * input.Quantity;
        }

        var atrPercent = input.EntryAtr / input.EntryPrice;
        var volatilityFactor = Math.Clamp(atrPercent / 0.02m, 0.5m, 3m);
        var liquidityFactor = 1m;
        if (input.EntryVolume > 0)
        {
            var orderRatio = (decimal)input.Quantity / input.EntryVolume;
            var squareRootImpact = (decimal)Math.Sqrt((double)Math.Max(0m, orderRatio));
            liquidityFactor = Math.Clamp(1m + squareRootImpact * 2m, 0.5m, 3m);
        }

        var adaptivePercent = slippagePercent / 100m * volatilityFactor * liquidityFactor;
        return (input.EntryPrice + input.ExitPrice) * adaptivePercent * input.Quantity;
    }
}
