using StockTrader.Application.Backtesting;
using StockTrader.ServiceContracts.Optimization;

namespace StockTrader.Application.Optimization;

public static class OptimizationPreparedDataFactory
{
    public static OptimizationPreparedDataSet Create(OptimizationEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var series = context.DataByTimeFrame.OrderBy(item => item.Key)
            .SelectMany(frame => frame.Value.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(symbol => CreateSeries(symbol.Key, frame.Key.ToString(), symbol.Value)))
            .ToArray();
        var regimes = context.Regimes.OrderBy(item => item.Key)
            .Select(item => new OptimizationRegimeSnapshot(
                item.Key,
                item.Value.SpyAbove200Ma,
                item.Value.SpyPrice,
                item.Value.Spy200Ma,
                item.Value.VixLevel,
                item.Value.RegimeLabel,
                Normalize(item.Value.AsOf),
                item.Value.MlClusterId,
                item.Value.MlRegimeLabel))
            .ToArray();
        var risk = new OptimizationRiskSnapshot(
            context.Risk.RiskPerTradePercent,
            context.Risk.DailyLossLimitPercent,
            context.Risk.MaxTotalPositions,
            context.Risk.MaxPositionsPerSector);
        var hash = OptimizationPreparedDataIdentity.Compute(series, regimes, risk);
        return new OptimizationPreparedDataSet(
            OptimizationWorkerContractCatalog.EvaluationInputVersion,
            hash,
            series,
            regimes,
            risk);
    }

    private static OptimizationPreparedSeries CreateSeries(
        string symbol,
        string timeFrame,
        PreparedSymbolData data) => new(
        symbol.Trim().ToUpperInvariant(),
        timeFrame,
        data.Bars.Select(bar => new OptimizationBar(
            Normalize(bar.Timestamp),
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.Vwap)).ToArray(),
        data.Atr.ToArray(),
        data.Closes.ToArray(),
        data.TqqqProtectiveStopFloor.ToArray(),
        data.CumulativeRsi2.ToArray(),
        data.CumulativeRsi2TrendMa.ToArray());

    private static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
