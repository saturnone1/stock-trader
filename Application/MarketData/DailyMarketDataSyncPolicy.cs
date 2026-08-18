using StockTrader.Application.Strategies;
using StockTrader.Domain.MarketData;

namespace StockTrader.Application.MarketData;

/// <summary>일봉 동기화 대상과 전략 평가에 필요한 최소 이력을 결정합니다.</summary>
public static class DailyMarketDataSyncPolicy
{
    public static IReadOnlyList<string> ResolveRequiredSymbols(
        IEnumerable<string> watchlistSymbols,
        DataSource source) =>
        MarketSymbolPolicy.NormalizeMany(
            watchlistSymbols.Append(DataProviderCatalog.RegimeBenchmarkSymbol(source)));

    public static int MinimumRequiredBars(string symbol, DataSource source) =>
        symbol.Equals(
            DataProviderCatalog.RegimeBenchmarkSymbol(source),
            StringComparison.OrdinalIgnoreCase)
            ? StrategyEvaluationPolicy.RegimeTrendBars
            : StrategyEvaluationPolicy.LiveScannerMinimumBars;
}
